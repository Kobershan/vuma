using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Sales.Invoices;
using VumaRetail.Infrastructure.Inventory;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Registry;

namespace VumaRetail.Infrastructure.Sales;

/// <summary>
/// Issues one posted invoice per supplying company for a single fulfilled order or sale (ADR-102).
/// </summary>
/// <remarks>
/// <para>
/// An application service, NOT a command handler: a handler may resolve at most one company
/// context, while an issue spans several — each segment posts in its own company scope, in its
/// own serialisable transaction, and this service touches only the registry itself (ADR-116).
/// </para>
/// <para>
/// Posted legs are terminal. A leg that fails after siblings posted leaves the intent in flight
/// for the in-flight report rather than pretending compensation happened: unwinding a posted
/// invoice is a Stage 10 credit note, a new document, never an edit here.
/// </para>
/// </remarks>
public sealed class InvoiceIssuingService : IInvoiceIssuingService
{
    /// <summary>The saga intent type for invoice issues.</summary>
    public const string IntentType = "sales.invoice-issue";

    private readonly VumaRegistryDbContext _registry;
    private readonly IServiceScopeFactory _scopes;
    private readonly ICompanyLinkGuard _links;
    private readonly IReplicationRegistry _replication;
    private readonly IReplicaWriter _replicas;
    private readonly IHybridClock _hybridClock;
    private readonly INodeIdentity _node;
    private readonly IClock _clock;
    private readonly ILogger<InvoiceIssuingService> _logger;

    /// <summary>Builds the service. All collaborators are scoped; legs run in child scopes.</summary>
    public InvoiceIssuingService(
        VumaRegistryDbContext registry,
        IServiceScopeFactory scopes,
        ICompanyLinkGuard links,
        IReplicationRegistry replication,
        IReplicaWriter replicas,
        IHybridClock hybridClock,
        INodeIdentity node,
        IClock clock,
        ILogger<InvoiceIssuingService> logger)
    {
        _registry = registry;
        _scopes = scopes;
        _links = links;
        _replication = replication;
        _replicas = replicas;
        _hybridClock = hybridClock;
        _node = node;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IssuedInvoice>> IssueAsync(
        InvoiceIssuingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureCoherent(request);

        SagaIntent? existing = await _registry.SagaIntents
            .Include(intent => intent.Legs)
            .FirstOrDefaultAsync(
                intent => intent.TenantId == request.TenantId
                    && intent.Type == IntentType
                    && intent.IdempotencyKey == request.IdempotencyKey.Trim(),
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return await ReplayAsync(existing, request, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<Guid> suppliers = request.Segments
            .Select(segment => segment.CompanyId)
            .Where(company => company != request.OrderingCompanyId)
            .Distinct()
            .OrderBy(company => company)
            .ToList();

        foreach (Guid supplier in suppliers)
        {
            await _links.RequireLinkAsync(
                    request.TenantId, request.OrderingCompanyId, supplier,
                    CompanyLinkScope.SharedSourcing, cancellationToken)
                .ConfigureAwait(false);
        }

        Company ordering = await _registry.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                company => company.TenantId == request.TenantId && company.Id == request.OrderingCompanyId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The ordering company is not registered.");

        if (ordering.OperatorId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The ordering company has no Operator ID; nothing may invoice across companies for it (ADR-121).");
        }

        SagaIntent intent = SagaIntent.Create(
            request.TenantId,
            IntentType,
            request.IdempotencyKey.Trim(),
            _clock.UtcNow,
            Serialise(request));
        intent.Authorize(ordering.OperatorId, request.InitiatedBy.Trim(), _hybridClock.Next().ToString());

        foreach (InvoiceCompanySegment segment in request.Segments.OrderBy(segment => segment.CompanyId))
        {
            intent.AddLeg(segment.CompanyId);
        }

        _registry.SagaIntents.Add(intent);
        await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

        intent.Start("invoice-issue");
        await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteAsync(intent, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<IssuedInvoice>> ExecuteAsync(
        SagaIntent intent, InvoiceIssuingRequest request, CancellationToken cancellationToken)
    {
        List<IssuedInvoice> issued = [];

        try
        {
            foreach (SagaLeg leg in intent.Legs.OrderBy(leg => leg.CompanyId))
            {
                InvoiceCompanySegment segment = request.Segments.First(s => s.CompanyId == leg.CompanyId);

                leg.MarkDispatched(_clock.UtcNow, intent.OperationStamp);
                IssuedInvoice invoice = await WriteLegAsync(leg, request, segment, cancellationToken)
                    .ConfigureAwait(false);
                issued.Add(invoice);
                leg.Acknowledge(_clock.UtcNow);

                await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);
            }

            intent.Complete();
            await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

            return issued;
        }
        catch (Exception failure)
        {
            _logger.LogWarning(
                failure,
                "Invoice issue {IntentId} failed after posting {Posted} of {Total} segments; the intent stays in flight.",
                intent.Id,
                issued.Count,
                intent.Legs.Count);

            throw new InvoiceIssuingFailedException(intent.Id, issued, failure.Message, failure);
        }
    }

    private async Task<IssuedInvoice> WriteLegAsync(
        SagaLeg leg,
        InvoiceIssuingRequest request,
        InvoiceCompanySegment segment,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopes.CreateScope();
        Bind(scope, request.TenantId, leg.CompanyId);

        await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(
                scope, CompanyAccessMode.Write, cancellationToken)
            .ConfigureAwait(false);

        ITenantContext scopedTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        Persistence.Interceptors.AuditStamper audit =
            scope.ServiceProvider.GetRequiredService<Persistence.Interceptors.AuditStamper>();

        await using var transaction = await companyDb.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var numbers = new DocumentNumberSequence(companyDb, scopedTenant);
            string invoiceNumber = await numbers.NextAsync("INV", cancellationToken).ConfigureAwait(false);

            Invoice invoice = Invoice.Create(
                request.TenantId,
                scopedTenant.StoreId,
                invoiceNumber,
                leg.CompanyId,
                request.SourceDocumentId.ToString(),
                request.SourceType,
                request.CustomerId,
                request.Currency,
                request.GroupDocumentRef,
                request.SettlementTerms);

            foreach (InvoiceLineInput input in segment.Lines)
            {
                invoice.AddLine(InvoiceLine.Create(
                    request.TenantId,
                    scopedTenant.StoreId,
                    invoice.Id,
                    input.ItemId,
                    input.ItemVariantId,
                    input.Quantity,
                    input.Uom,
                    new Money(input.UnitPrice, request.Currency),
                    new Money(input.DiscountAmount, request.Currency),
                    new Money(input.TaxAmount, request.Currency),
                    input.PackSizeDescription,
                    request.Currency,
                    input.PriceListId));
            }

            invoice.Post(_clock.UtcNow);

            await scope.ServiceProvider
                .GetRequiredService<Application.Sales.IInvoiceFinancialEventPublisher>()
                .PublishAsync(
                    new Application.Sales.InvoicePostedEvent(
                        request.TenantId,
                        scopedTenant.StoreId,
                        leg.CompanyId,
                        invoice.Id,
                        invoiceNumber,
                        request.SourceType,
                        invoice.Net,
                        invoice.Tax,
                        invoice.Gross,
                        invoice.PostedAt!.Value),
                    cancellationToken)
                .ConfigureAwait(false);

            companyDb.Invoices.Add(invoice);
            foreach (InvoiceLine line in invoice.Lines)
            {
                companyDb.InvoiceLines.Add(line);
            }

            CompanyOutboxCapture.Capture(
                companyDb, _replication, _replicas, _hybridClock, _node, _clock, invoice);
            foreach (InvoiceLine line in invoice.Lines)
            {
                CompanyOutboxCapture.Capture(
                    companyDb, _replication, _replicas, _hybridClock, _node, _clock, line);
            }

            audit.Stamp(companyDb);
            await companyDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            audit.Complete();

            _logger.LogDebug(
                "Invoice {InvoiceNumber} posted in company {CompanyId} for {SourceRef}.",
                invoiceNumber,
                leg.CompanyId,
                request.SourceDocumentId);

            return new IssuedInvoice(leg.CompanyId, invoice.Id, invoiceNumber);
        }
        catch (Exception failure)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            leg.Fail(failure.Message);
            throw new InvoiceLegFailedException(leg.CompanyId, leg.LegId, failure.Message, failure);
        }
    }

    private async Task<IReadOnlyList<IssuedInvoice>> ReplayAsync(
        SagaIntent existing, InvoiceIssuingRequest request, CancellationToken cancellationToken)
    {
        if (existing.State == SagaIntentState.Completed
            || existing.Legs.All(leg => leg.State == SagaLegState.Acknowledged))
        {
            if (existing.State != SagaIntentState.Completed)
            {
                existing.Complete();
                await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);
            }

            List<IssuedInvoice> issued = [];
            foreach (SagaLeg leg in existing.Legs.OrderBy(leg => leg.CompanyId))
            {
                IReadOnlyList<IssuedInvoice> found = await FindIssuedAsync(
                    request, leg.CompanyId, cancellationToken).ConfigureAwait(false);
                issued.AddRange(found);
            }

            return issued;
        }

        throw new InvoiceIssuingConflictException(
            existing.Id,
            existing.State,
            "A previous issue under this key did not complete. Posted segments stay posted; unwind them with credit notes, then retry with a new idempotency key.");
    }

    private async Task<IReadOnlyList<IssuedInvoice>> FindIssuedAsync(
        InvoiceIssuingRequest request, Guid companyId, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopes.CreateScope();
        Bind(scope, request.TenantId, companyId);

        await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(
                scope, CompanyAccessMode.Read, cancellationToken)
            .ConfigureAwait(false);

        string sourceRef = request.SourceDocumentId.ToString();

        return await companyDb.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.CompanyId == companyId)
            .Where(invoice => invoice.SourceDocumentRef == sourceRef)
            .Where(invoice => request.GroupDocumentRef == null || invoice.GroupDocumentRef == request.GroupDocumentRef)
            .OrderBy(invoice => invoice.InvoiceNumber)
            .Select(invoice => new IssuedInvoice(companyId, invoice.Id, invoice.InvoiceNumber))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // The file's single company-context acquisition (MultiCompanyGuardTests counts textual
    // .CreateAsync occurrences per file): every leg — write or replay-read — opens its one
    // company database here, in its own scope, never two in one place.
    private static Task<VumaRetailDbContext> OpenCompanyDbAsync(
        IServiceScope scope, CompanyAccessMode access, CancellationToken cancellationToken)
    {
        ICompanyDbContextFactory companies = scope.ServiceProvider.GetRequiredService<ICompanyDbContextFactory>();
        return companies.CreateAsync(access, cancellationToken);
    }

    private static void Bind(IServiceScope scope, Guid tenantId, Guid companyId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An invoice leg needs its tenant.", nameof(tenantId));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("An invoice leg needs its company.", nameof(companyId));
        }

        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);
    }

    private async Task SaveRegistryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _registry.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres
            && postgres.SqlState is "23505")
        {
            throw new InvoiceIssuingConflictException(
                Guid.Empty,
                SagaIntentState.Pending,
                "This issue was already recorded. Retry with the same key to replay it, or a new key to re-plan.");
        }
    }

    private static void EnsureCoherent(InvoiceIssuingRequest request)
    {
        if (request.TenantId == Guid.Empty)
        {
            throw new ArgumentException("An issue needs its tenant.", nameof(request));
        }

        if (request.OrderingCompanyId == Guid.Empty)
        {
            throw new ArgumentException("An issue needs its ordering company.", nameof(request));
        }

        if (request.SourceDocumentId == Guid.Empty)
        {
            throw new ArgumentException("An issue needs its source document.", nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceDocumentNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InitiatedBy);

        if (request.CustomerId == Guid.Empty)
        {
            throw new ArgumentException("An issue needs its customer.", nameof(request));
        }

        if (request.Segments.Count == 0)
        {
            throw new ArgumentException("An issue needs at least one company segment.", nameof(request));
        }

        HashSet<Guid> companies = [];
        foreach (InvoiceCompanySegment segment in request.Segments)
        {
            if (segment.CompanyId == Guid.Empty)
            {
                throw new ArgumentException("Every segment needs its company.", nameof(request));
            }

            if (!companies.Add(segment.CompanyId))
            {
                throw new InvoicesRuleException(
                    "INVOICE_SPLIT_DUPLICATE_COMPANY",
                    $"Company {segment.CompanyId} carries two segments. One company, one invoice.");
            }

            if (segment.Lines.Count == 0)
            {
                throw new InvoicesRuleException(
                    "INVOICE_NO_LINES", $"Company {segment.CompanyId}'s segment has no lines.");
            }

            foreach (InvoiceLineInput line in segment.Lines)
            {
                EnsureLineCoherent(request, segment.CompanyId, line);
            }
        }

        HashSet<(Guid Company, Guid Line)> seen = [];
        foreach (InvoiceCompanySegment segment in request.Segments)
        {
            foreach (InvoiceLineInput line in segment.Lines)
            {
                if (line.SourceLineId is { } sourceLine && !seen.Add((segment.CompanyId, sourceLine)))
                {
                    throw new InvoicesRuleException(
                        "INVOICE_SPLIT_DUPLICATE_LINE",
                        $"Source line {sourceLine} lands twice on company {segment.CompanyId}'s invoice.");
                }
            }
        }

        decimal gross = request.Segments
            .SelectMany(segment => segment.Lines)
            .Sum(line => (line.UnitPrice * line.Quantity) - line.DiscountAmount + line.TaxAmount);
        if (gross == 0m)
        {
            throw InvoicesRuleException.CannotSplitZeroAmount();
        }
    }

    private static void EnsureLineCoherent(
        InvoiceIssuingRequest request, Guid companyId, InvoiceLineInput line)
    {
        if (line.ItemId.HasValue == line.ItemVariantId.HasValue)
        {
            throw InvoicesRuleException.ExactlyOneItemOrVariantRequired();
        }

        if (line.Quantity <= 0m)
        {
            throw InvoicesRuleException.QuantityMustBePositive();
        }

        if (string.IsNullOrWhiteSpace(line.Uom))
        {
            throw new InvoicesRuleException(
                "INVOICE_LINE_UOM_REQUIRED", $"Company {companyId}'s invoice line needs its unit of measure.");
        }

        if (line.UnitPrice < 0m || line.DiscountAmount < 0m || line.TaxAmount < 0m)
        {
            throw new InvoicesRuleException(
                "INVOICE_LINE_AMOUNTS_NOT_NEGATIVE", $"Company {companyId}'s invoice line amounts cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(line.PackSizeDescription))
        {
            throw InvoicesRuleException.PackSizeNotResolved();
        }
    }

    private static string Serialise(InvoiceIssuingRequest request)
        => JsonSerializer.Serialize(new
        {
            sourceDocumentId = request.SourceDocumentId,
            sourceDocumentNumber = request.SourceDocumentNumber,
            orderingCompany = request.OrderingCompanyId,
            segments = request.Segments.Select(segment => new
            {
                company = segment.CompanyId,
                lines = segment.Lines.Count,
            }).ToArray(),
        });
}

/// <summary>The issue failed after validation; every posted segment stays posted.</summary>
/// <param name="IntentId">The in-flight intent.</param>
/// <param name="Posted">The segments that posted before the failure.</param>
/// <param name="Reason">Which leg failed and why.</param>
/// <param name="Inner">The leg failure.</param>
public sealed class InvoiceIssuingFailedException(
    Guid IntentId, IReadOnlyList<IssuedInvoice> Posted, string Reason, Exception? Inner = null)
    : InvalidOperationException(
        $"Invoice issue {IntentId} failed after posting {Posted.Count} segment(s): {Reason}", Inner);

/// <summary>One invoice leg failed hard (the company, not the arithmetic).</summary>
/// <param name="CompanyId">The failed leg's company.</param>
/// <param name="LegId">The failed leg.</param>
/// <param name="Reason">Why it failed.</param>
/// <param name="Inner">The underlying failure.</param>
public sealed class InvoiceLegFailedException(Guid CompanyId, Guid LegId, string Reason, Exception? Inner = null)
    : InvalidOperationException($"Invoice leg {LegId} in company {CompanyId} failed: {Reason}", Inner);

/// <summary>An issue key is already in use by an incomplete attempt, or raced a duplicate.</summary>
/// <param name="IntentId">The existing intent, or empty when it could not be loaded.</param>
/// <param name="State">The existing intent's state.</param>
/// <param name="Advice">What the caller should do.</param>
public sealed class InvoiceIssuingConflictException(Guid IntentId, SagaIntentState State, string Advice)
    : InvalidOperationException($"Invoice issue conflict on intent {IntentId} ({State}): {Advice}");
