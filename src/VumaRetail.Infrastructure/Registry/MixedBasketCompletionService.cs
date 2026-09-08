using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Pos;
using VumaRetail.Application.Sales;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Registry.Trading;
using VumaRetail.Domain.Sales;
using VumaRetail.Domain.Sales.Invoices;
using VumaRetail.Finance.Posting;
using VumaRetail.Infrastructure.Inventory;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Interceptors;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>
/// Completes a tendered trading session: one leg per company segment, each writing a complete
/// sale, tax invoice and receipt in its own company's database (Stage 09b, ADR-125/ADR-126).
/// </summary>
/// <remarks>
/// <para>
/// An application service, NOT a command handler: a handler may resolve at most one company
/// context, while a completion spans several — each segment posts in its own company scope, in
/// its own serialisable transaction, and this service touches only the registry itself (ADR-116).
/// The saga shape mirrors <c>InvoiceIssuingService</c> deliberately: intent with an idempotency
/// key, link checks before any write, legs in company order, replay returns stored results.
/// </para>
/// <para>
/// Two deliberate differences from the 10c precedent (ADR-145). First, journals post through a
/// company-database-bound <c>PostingRuleEngine</c> constructed in the leg, not the ambient
/// pipeline's engine — a sister company's journal must land in the sister company's books.
/// Second, a failed leg compensates posted siblings with new reversing documents (full sales
/// return, reversal receipt, released holds) and returns the session to tendered-with-reason;
/// posted invoices that cannot auto-credit are listed for back-office credit-noting because
/// Stage 10's invoice credit note does not exist yet.
/// </para>
/// </remarks>
public sealed class MixedBasketCompletionService : IMixedBasketCompletionService
{
    /// <summary>The saga intent type for trading-session completions.</summary>
    public const string IntentType = "trading-session-complete";

    /// <summary>The event type a leg's receipt posts. Seeded per company with the sale/invoice rules.</summary>
    public const string ReceiptSettledEventType = "trading.session.receipt.settled";

    /// <summary>The event type a compensation leg posts. Seeded per company; missing rules log per ADR-070.</summary>
    public const string LegReversedEventType = "trading.session.leg-reversed";

    private readonly VumaRegistryDbContext _registry;
    private readonly ITradingSessionRepository _sessions;
    private readonly ITradingCompanyGateway _gateway;
    private readonly IServiceScopeFactory _scopes;
    private readonly ICompanyLinkService _links;
    private readonly IReplicationRegistry _replication;
    private readonly IReplicaWriter _replicas;
    private readonly IHybridClock _hybridClock;
    private readonly INodeIdentity _node;
    private readonly IClock _clock;
    private readonly ILogger<MixedBasketCompletionService> _logger;

    /// <summary>Builds the service. All collaborators are scoped; legs run in child scopes.</summary>
    public MixedBasketCompletionService(
        VumaRegistryDbContext registry,
        ITradingSessionRepository sessions,
        ITradingCompanyGateway gateway,
        IServiceScopeFactory scopes,
        ICompanyLinkService links,
        IReplicationRegistry replication,
        IReplicaWriter replicas,
        IHybridClock hybridClock,
        INodeIdentity node,
        IClock clock,
        ILogger<MixedBasketCompletionService> logger)
    {
        _registry = registry;
        _sessions = sessions;
        _gateway = gateway;
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
    public async Task<IReadOnlyList<CompletedSegment>> CompleteAsync(
        Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("A completion needs its session.", nameof(sessionId));
        }

        TradingSession session = await _sessions.FindAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new TradingSessionException(
                "TRADING_SESSION_NOT_FOUND", $"No trading session {sessionId}.");

        // A replayed completion returns the same invoice numbers and creates nothing (§4.11).
        // The segments carry their posted results, so a replay never touches a company
        // database — including the single-segment fast path, which wrote no intent row.
        if (session.Status is TradingSessionStatus.Completed)
        {
            List<CompletedSegment> replayed = [];
            foreach (TradingSessionSegment segment in session.Segments.Where(s => !s.IsRemoved))
            {
                CompletedSegment? posted = FindPosted(session, segment.CompanyId);
                if (posted is null)
                {
                    throw new TradingSessionConflictException(
                        Guid.Empty,
                        $"Session {session.SessionNumber} reads completed but company {segment.CompanyId} carries no posted result.");
                }

                replayed.Add(posted);
            }

            return replayed;
        }

        if (session.Status is not TradingSessionStatus.Tendered
            and not TradingSessionStatus.CompletionFailed
            and not TradingSessionStatus.Completing)
        {
            throw TradingSessionException.IllegalTransition(session.Status, "complete");
        }

        List<TradingSessionSegment> segments = [.. session.Segments.Where(s => !s.IsRemoved && s.LiveLines.Any())];
        if (segments.Count == 0)
        {
            throw new TradingSessionException(
                "TRADING_EMPTY_BASKET", "A session with no live lines cannot complete.");
        }

        if (session.TenderAmount is not { } tender)
        {
            throw new TradingSessionException(
                "TRADING_TENDER_MISSING", "A session captures its tender before it completes.");
        }

        // Rule 12: a single-company basket takes no registry path and behaves exactly as a
        // Stage 09 sale does today. One direct leg, no intent row — asserted by test with a
        // saga-intent count, so the common case never pays for the mixed one.
        Dictionary<Guid, Guid> locations = [];
        foreach (TradingSessionSegment segment in segments)
        {
            locations[segment.CompanyId] = await _gateway.ResolveDefaultLocationAsync(
                    session.TenantId, segment.CompanyId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (segments.Count == 1 && segments[0].CompanyId == session.SessionCompanyId)
        {
            CompletedSegment single = await ExecuteLegAsync(
                    null, null, session, segments[0], locations[segments[0].CompanyId],
                    session.TenderReference, cancellationToken)
                .ConfigureAwait(false);
            session.MarkCompleting();
            session.MarkCompleted(
                [(single.CompanyId, single.SaleId, single.InvoiceId, single.InvoiceNumber)],
                _clock.UtcNow);
            await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);
            return [single];
        }

        // Links lapse between scan and payment (TRADING_GROUP.md §2): re-check every sister
        // segment here, at completion time, before anything writes. A lapsed company refuses
        // with the scope named while the others stay untouched.
        foreach (TradingSessionSegment segment in segments.Where(s => s.CompanyId != session.SessionCompanyId))
        {
            await _links.RequireLink(
                    session.SessionCompanyId, segment.CompanyId,
                    CompanyLinkScope.SharedTill, cancellationToken)
                .ConfigureAwait(false);
        }

        Company ordering = await _registry.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                company => company.TenantId == session.TenantId && company.Id == session.SessionCompanyId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The session company is not registered.");

        if (ordering.OperatorId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The session company has no Operator ID; nothing may settle across companies for it (ADR-121).");
        }

        SagaIntent? existing = await _registry.SagaIntents
            .Include(intent => intent.Legs)
            .FirstOrDefaultAsync(
                intent => intent.TenantId == session.TenantId
                    && intent.Type == IntentType
                    && intent.IdempotencyKey == session.IdempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return await ReplayAsync(existing, session, cancellationToken).ConfigureAwait(false);
        }

        SagaIntent intent = SagaIntent.Create(
            session.TenantId,
            IntentType,
            session.IdempotencyKey,
            _clock.UtcNow,
            Serialise(session));
        intent.Authorize(ordering.OperatorId, session.CashierUserId.ToString(), _hybridClock.Next().ToString());

        foreach (TradingSessionSegment segment in segments.OrderBy(s => s.CompanyId))
        {
            intent.AddLeg(segment.CompanyId);
        }

        _registry.SagaIntents.Add(intent);
        await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

        session.MarkCompleting();
        intent.Start("trading-session-complete");
        await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteAsync(intent, session, segments, locations, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CompletedSegment>> ExecuteAsync(
        SagaIntent intent,
        TradingSession session,
        List<TradingSessionSegment> segments,
        Dictionary<Guid, Guid> locations,
        CancellationToken cancellationToken)
    {
        List<CompletedSegment> posted = [];

        try
        {
            foreach (SagaLeg leg in intent.Legs.OrderBy(leg => leg.CompanyId))
            {
                if (leg.State == SagaLegState.Acknowledged)
                {
                    CompletedSegment? resumed = FindPosted(session, leg.CompanyId);
                    if (resumed is not null)
                    {
                        posted.Add(resumed);
                        continue;
                    }
                }

                TradingSessionSegment segment = segments.First(s => s.CompanyId == leg.CompanyId);

                leg.MarkDispatched(_clock.UtcNow, intent.OperationStamp);
                CompletedSegment done = await ExecuteLegAsync(
                    leg, leg.IntentId, session, segment, locations[segment.CompanyId],
                    session.TenderReference, cancellationToken)
                    .ConfigureAwait(false);
                posted.Add(done);
                leg.Acknowledge(_clock.UtcNow);

                await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);
            }

            session.MarkCompleted(
                [.. posted.Select(p => (p.CompanyId, p.SaleId, p.InvoiceId, p.InvoiceNumber))],
                _clock.UtcNow);
            intent.Complete();
            await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

            return posted;
        }
        catch (Exception failure)
        {
            _logger.LogWarning(
                failure,
                "Trading session {SessionNumber} failed after posting {Posted} of {Total} segments; compensating.",
                session.SessionNumber,
                posted.Count,
                intent.Legs.Count);

            IReadOnlyList<string> unwound = await CompensateAsync(
                    intent, session, posted, failure.Message, cancellationToken)
                .ConfigureAwait(false);

            intent.Compensate();
            session.MarkCompletionFailed(failure.Message, unwound);
            await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

            throw new TradingSessionFailedException(session.Id, intent.Id, posted, failure.Message, failure);
        }
    }

    private async Task<CompletedSegment> ExecuteLegAsync(
        SagaLeg? leg,
        Guid? intentId,
        TradingSession session,
        TradingSessionSegment segment,
        Guid locationId,
        string? reference,
        CancellationToken cancellationToken)
    {
        Guid companyId = segment.CompanyId;
        Money allocation = segment.TenderAllocation
            ?? throw new TradingSessionException(
                "TRADING_ALLOCATION_MISSING", $"Company {companyId}'s segment carries no tender allocation.");

        // Round 1: hold every line in the owning company (ADR-102). A shortfall fails the leg
        // before a single sale, invoice or receipt row exists — the clean failure, needing no
        // compensation, and the reason the saga never oversells (available never negative).
        Dictionary<Guid, Guid> holds = [];
        foreach (TradingSessionLine line in segment.LiveLines)
        {
            Guid? itemId = line.ItemId == Guid.Empty ? null : line.ItemId;
            ReserveOutcome outcome = await _gateway.RunReservationAsync(
                    session.TenantId,
                    companyId,
                    provider => provider.GetRequiredService<IReservationService>().ReserveAsync(
                        locationId,
                        itemId,
                        line.ItemVariantId,
                        new Quantity(line.QuantityValue, line.QuantityUom),
                        ReservationSource.MixedBasket,
                        session.Id,
                        session.SessionNumber,
                        null,
                        leg?.IntentId,
                        leg?.LegId,
                        $"Trading session {session.SessionNumber}",
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            if (outcome.ReservationId is null || outcome.Shortfall.Value > 0m)
            {
                foreach (Guid held in holds.Values)
                {
                    await _gateway.RunReservationAsync(
                            session.TenantId, companyId,
                            provider => provider.GetRequiredService<IReservationService>()
                                .ReleaseAsync(held, $"Trading session {session.SessionNumber} shortfall", cancellationToken),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                throw new TradingLegFailedException(
                    companyId, leg?.LegId ?? Guid.Empty,
                    $"Company {companyId} cannot cover {line.Description}: short {outcome.Shortfall.Value} {line.QuantityUom}.");
            }

            holds[line.Id] = outcome.ReservationId.Value;
        }

        // Round 2: the money transaction — sale, invoice and receipt atomically, in the
        // company's own database, through a company-bound posting engine (ADR-145).
        LegDocuments documents;
        try
        {
            documents = await WriteMoneyAsync(session, segment, companyId, locationId, allocation, reference, intentId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            foreach (Guid held in holds.Values)
            {
                await _gateway.RunReservationAsync(
                        session.TenantId, companyId,
                        provider => provider.GetRequiredService<IReservationService>()
                            .ReleaseAsync(held, $"Trading session {session.SessionNumber} money-leg failure", cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            leg?.Fail(failure.Message);
            throw new TradingLegFailedException(companyId, leg?.LegId ?? Guid.Empty, failure.Message, failure);
        }

        // Round 3: consume the holds against the posted sale. The books already balance; a
        // consume failure leaves availability conservative (reserved, not oversold) and the
        // leg reports it rather than pretending the stock moved.
        foreach ((Guid lineId, Guid reservationId) in holds)
        {
            try
            {
                await _gateway.RunReservationAsync(
                        session.TenantId, companyId,
                        provider => provider.GetRequiredService<IReservationService>()
                            .ConsumeAsync(reservationId, documents.SaleId, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                leg?.Fail($"Posted but not consumed: {failure.Message}");
                throw new TradingLegFailedException(
                    companyId, leg?.LegId ?? Guid.Empty,
                    $"Company {companyId}'s segment posted {documents.InvoiceNumber} but its holds did not consume: {failure.Message}. " +
                    "The money stands; retry the completion to heal availability.",
                    failure);
            }
        }

        segment.MarkPosted(documents.SaleId, documents.InvoiceId, documents.InvoiceNumber);
        return new CompletedSegment(companyId, documents.SaleId, documents.InvoiceId, documents.InvoiceNumber);
    }

    private async Task<LegDocuments> WriteMoneyAsync(
        TradingSession session,
        TradingSessionSegment segment,
        Guid companyId,
        Guid locationId,
        Money allocation,
        string? reference,
        Guid? intentId,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopes.CreateScope();
        Bind(scope, session.TenantId, companyId);

        await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(
                scope, CompanyAccessMode.Write, cancellationToken)
            .ConfigureAwait(false);

        ITenantContext scopedTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        AuditStamper audit = scope.ServiceProvider.GetRequiredService<AuditStamper>();
        ILoggerFactory loggers = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        // Deterministic document ids: a retried leg finds its own rows instead of minting
        // second ones (the §4.11 lesson, applied to every document this leg writes).
        Guid saleId = LegDocumentId(session.Id, companyId, "sale");
        Guid invoiceId = LegDocumentId(session.Id, companyId, "invoice");
        Guid receiptId = LegDocumentId(session.Id, companyId, "receipt");
        Guid tenderId = LegDocumentId(session.Id, companyId, "tender");

        Sale? replayed = await companyDb.Sales
            .Include(sale => sale.Lines)
            .FirstOrDefaultAsync(sale => sale.Id == saleId, cancellationToken)
            .ConfigureAwait(false);
        if (replayed is not null && replayed.Status == SaleStatus.Completed)
        {
            Invoice? replayedInvoice = await companyDb.Invoices
                .FirstOrDefaultAsync(invoice => invoice.Id == invoiceId, cancellationToken)
                .ConfigureAwait(false);
            if (replayedInvoice is not null)
            {
                return new LegDocuments(replayed.Id, replayedInvoice.Id, replayedInvoice.InvoiceNumber);
            }
        }

        if (!Enum.TryParse<Domain.Pos.TenderType>(session.TenderType, ignoreCase: true, out Domain.Pos.TenderType tenderType))
        {
            throw new TradingSessionException(
                "TRADING_TENDER_UNKNOWN", $"Tender type '{session.TenderType}' is not a till tender.");
        }

        await using var transaction = await companyDb.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            StockLocation? location = await companyDb.StockLocations
                .FirstOrDefaultAsync(candidate => candidate.Id == locationId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new TradingSessionException(
                    "TRADING_NO_LOCATION", $"Company {companyId} has no stock location to sell from.");

            TillSession till = await companyDb.TillSessions
                .FirstOrDefaultAsync(
                    open => open.TerminalId == session.TerminalId
                        && open.CompanyId == companyId
                        && open.Status == TillSessionStatus.Open,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? OpenTill(companyDb, session, companyId, _clock.UtcNow);

            var numbers = new DocumentNumberSequence(companyDb, scopedTenant);
            string saleNumber = await numbers.NextAsync("SALE", cancellationToken).ConfigureAwait(false);

            Guid customerId = session.CustomerGroupPartnerId ?? Guid.Empty;
            Sale sale = Sale.Open(
                saleId, session.TenantId, scopedTenant.StoreId, saleNumber, till,
                session.CashierUserId, location.Id, session.CustomerGroupPartnerId,
                session.Currency, _clock.UtcNow);
            sale.AssignCompany(companyId);

            int lineNumber = 0;
            foreach (TradingSessionLine line in segment.LiveLines)
            {
                sale.AddLine(SaleLine.Ring(
                    session.TenantId,
                    scopedTenant.StoreId,
                    sale.Id,
                    ++lineNumber,
                    line.ItemId == Guid.Empty ? null : line.ItemId,
                    line.ItemVariantId,
                    line.Description,
                    new Quantity(line.QuantityValue, line.QuantityUom),
                    line.UnitPrice,
                    line.DiscountAmount,
                    line.TaxCode,
                    line.Net,
                    line.TaxAmount,
                    line.Net + line.TaxAmount,
                    TradingLegIds.SaleLineId(session.Id, companyId, line.Id)));
            }

            sale.AddTender(SaleTender.Capture(
                session.TenantId, scopedTenant.StoreId, sale.Id,
                tenderType, allocation, reference, _clock.UtcNow, tenderId));
            sale.Complete(_clock.UtcNow);

            // The stock issue per line, Refused-not-fatal exactly like a Stage 09 till (R1,
            // ADR-073): the shelf wins over the system and the discrepancy becomes a
            // reconcilable row rather than a turned-away customer.
            StockLedgerPoster stock = LegStockPoster(companyDb, scope, loggers);
            foreach (SaleLine saleLine in sale.LiveLines)
            {
                try
                {
                    StockLedgerEntry entry = await stock.IssueForSaleAsync(
                            location, saleLine.ItemId, saleLine.ItemVariantId,
                            new Quantity(saleLine.Quantity.Value, saleLine.Quantity.UnitOfMeasure),
                            sale.Id, cancellationToken)
                        .ConfigureAwait(false);
                    saleLine.RecordStockIssued(entry.Id);
                }
                catch (InventoryRuleException refusal)
                {
                    saleLine.RecordStockIssueRefused(refusal.Message);
                }
            }

            string invoiceNumber = await numbers.NextAsync("INV", cancellationToken).ConfigureAwait(false);
            Invoice invoice = Invoice.Create(
                session.TenantId,
                scopedTenant.StoreId,
                invoiceNumber,
                companyId,
                sale.Id.ToString(),
                InvoiceSourceType.Sale,
                customerId,
                session.Currency,
                session.SessionNumber);

            foreach (TradingSessionLine line in segment.LiveLines)
            {
                // The invoice model prices exclusive (Net = unit × qty − discount): the till
                // captures inclusive shelf prices, so the exclusive unit is derived back out of
                // the captured snapshots — (Net + Discount) / qty — and the line totals land
                // exactly on the segment's taxed figures by construction (ADR-145).
                Money exclusiveUnit = (line.Net + line.DiscountAmount) / line.QuantityValue;
                invoice.AddLine(InvoiceLine.Create(
                    session.TenantId,
                    scopedTenant.StoreId,
                    invoice.Id,
                    line.ItemId == Guid.Empty ? null : line.ItemId,
                    line.ItemVariantId,
                    line.QuantityValue,
                    line.QuantityUom,
                    exclusiveUnit,
                    line.DiscountAmount,
                    line.TaxAmount,
                    line.PackSizeDescription,
                    session.Currency,
                    line.PriceListId));
            }

            invoice.Post(_clock.UtcNow);

            PostingRuleEngine engine = LegEngine(companyDb, scopedTenant, loggers);

            await new FinancialSaleEventPublisher(engine, loggers.CreateLogger<FinancialSaleEventPublisher>())
                .PublishAsync(
                    new SaleTenderedEvent(
                        session.TenantId, scopedTenant.StoreId, sale.Id, saleNumber,
                        sale.Net, sale.Tax, sale.Gross, sale.CompletedAt ?? _clock.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);

            await new FinancialInvoiceEventPublisher(engine, loggers.CreateLogger<FinancialInvoiceEventPublisher>())
                .PublishAsync(
                    new InvoicePostedEvent(
                        session.TenantId, scopedTenant.StoreId, companyId, invoice.Id, invoiceNumber,
                        InvoiceSourceType.Sale, invoice.Net, invoice.Tax, invoice.Gross,
                        invoice.PostedAt ?? _clock.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);

            string receiptNumber = await numbers.NextAsync("ARREC", cancellationToken).ConfigureAwait(false);
            Guid receiptJournalId = await engine.PostAsync(
                    new FinancialEvent(
                        ReceiptSettledEventType,
                        session.TenantId,
                        scopedTenant.StoreId,
                        _clock.UtcNow,
                        receiptNumber,
                        new Dictionary<string, Money>(StringComparer.Ordinal)
                        {
                            ["Principal"] = allocation,
                        }),
                    cancellationToken)
                .ConfigureAwait(false);

            ArReceipt receipt = ArReceipt.RecordFromGroup(
                session.TenantId,
                scopedTenant.StoreId,
                PartnerId.From(customerId),
                receiptNumber,
                _clock.UtcNow,
                allocation,
                receiptJournalId,
                [(invoice.Id, allocation)],
                session.Id,
                intentId,
                bankAccountId: null);
            SetEntityId(receipt, receiptId);

            companyDb.TillSessions.Add(till);
            companyDb.Sales.Add(sale);
            foreach (SaleLine saleLine in sale.Lines)
            {
                saleLine.AssignCompany(companyId);
                companyDb.SaleLines.Add(saleLine);
            }

            foreach (SaleTender saleTender in sale.Tenders)
            {
                saleTender.AssignCompany(companyId);
                companyDb.SaleTenders.Add(saleTender);
            }

            // A deterministic id doubles as the replay guard: re-adding a tracked row with a
            // taken key throws, while a retried leg that committed reads its rows above.
            SetEntityId(invoice, invoiceId);
            companyDb.Invoices.Add(invoice);
            foreach (InvoiceLine invoiceLine in invoice.Lines)
            {
                invoiceLine.AssignCompany(companyId);
                companyDb.InvoiceLines.Add(invoiceLine);
            }

            receipt.AssignCompany(companyId);
            companyDb.ArReceipts.Add(receipt);
            foreach (ArReceiptAllocation allocationRow in receipt.Allocations)
            {
                allocationRow.AssignCompany(companyId);
                companyDb.ArReceiptAllocations.Add(allocationRow);
            }

            CompanyOutboxCapture.Capture(
                companyDb, _replication, _replicas, _hybridClock, _node, _clock, sale);
            CompanyOutboxCapture.Capture(
                companyDb, _replication, _replicas, _hybridClock, _node, _clock, invoice);
            CompanyOutboxCapture.Capture(
                companyDb, _replication, _replicas, _hybridClock, _node, _clock, receipt);

            audit.Stamp(companyDb);
            await companyDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            audit.Complete();

            _logger.LogDebug(
                "Trading session {SessionNumber} posted sale {SaleNumber} and invoice {InvoiceNumber} in company {CompanyId}.",
                session.SessionNumber, saleNumber, invoiceNumber, companyId);

            return new LegDocuments(sale.Id, invoice.Id, invoiceNumber);
        }
        catch (Exception failure)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new TradingLegFailedException(companyId, Guid.Empty, failure.Message, failure);
        }
    }

    private async Task<IReadOnlyList<string>> CompensateAsync(
        SagaIntent intent,
        TradingSession session,
        List<CompletedSegment> posted,
        string reason,
        CancellationToken cancellationToken)
    {
        List<string> unwound = [];

        // Reverse leg order: the last company to take money is the first to give it back.
        foreach (CompletedSegment segment in posted.OrderByDescending(p => p.CompanyId))
        {
            try
            {
                await CompensateLegAsync(session, segment, reason, cancellationToken).ConfigureAwait(false);
                SagaLeg? leg = intent.Legs.FirstOrDefault(l => l.CompanyId == segment.CompanyId);
                leg?.Compensate();
            }
            catch (Exception failure)
            {
                // Compensation itself must not abort the remaining legs: every posted company
                // gets its reversing attempt, and whatever still stands is named, not hidden.
                _logger.LogError(
                    failure,
                    "Compensation of trading session {SessionNumber} segment {CompanyId} failed.",
                    session.SessionNumber, segment.CompanyId);
            }

            unwound.Add(segment.InvoiceNumber);
            TradingSessionSegment? state = session.Segments.FirstOrDefault(s => s.CompanyId == segment.CompanyId);
            state?.MarkCompensated();
        }

        await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);
        return unwound;
    }

    private async Task CompensateLegAsync(
        TradingSession session,
        CompletedSegment posted,
        string reason,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopes.CreateScope();
        Bind(scope, session.TenantId, posted.CompanyId);

        await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(
                scope, CompanyAccessMode.Write, cancellationToken)
            .ConfigureAwait(false);

        ITenantContext scopedTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        AuditStamper audit = scope.ServiceProvider.GetRequiredService<AuditStamper>();
        ILoggerFactory loggers = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        Sale sale = await companyDb.Sales
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == posted.SaleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Compensation found no sale {posted.SaleId}.");

        TradingSessionSegment segment = session.Segments.First(s => s.CompanyId == posted.CompanyId);
        Money allocation = segment.TenderAllocation
            ?? throw new InvalidOperationException("A posted segment always carries its allocation.");

        await using var transaction = await companyDb.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var numbers = new DocumentNumberSequence(companyDb, scopedTenant);
            string compensationRef = $"TS-{session.SessionNumber} failed: {reason}";

            // Reversal document 1: a full sales return in the origin company (a completed sale
            // is void-proof by Stage 09 rule 6 — corrected by a return, never an edit).
            string returnNumber = await numbers.NextAsync("RTN", cancellationToken).ConfigureAwait(false);
            SalesReturn salesReturn = SalesReturn.Raise(
                sale,
                returnNumber,
                compensationRef,
                MapTender(session.TenderType),
                session.CashierUserId,
                _clock.UtcNow,
                LegDocumentId(session.Id, posted.CompanyId, "comp-return"));

            foreach (SaleLine soldLine in sale.LiveLines)
            {
                salesReturn.AddLine(
                    soldLine,
                    new Quantity(soldLine.Quantity.Value, soldLine.Quantity.UnitOfMeasure),
                    previouslyReturned: 0m);
            }

            salesReturn.Complete(_clock.UtcNow);

            PostingRuleEngine engine = LegEngine(companyDb, scopedTenant, loggers);

            await new FinancialSalesReturnEventPublisher(engine, loggers.CreateLogger<FinancialSalesReturnEventPublisher>())
                .PublishAsync(
                    new SalesReturnCompletedEvent(
                        session.TenantId, scopedTenant.StoreId, salesReturn.Id, returnNumber,
                        sale.Id, salesReturn.Net, salesReturn.Tax, salesReturn.Gross, _clock.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);

            // Reversal document 2: the receipt, unwound. Money speaks negatives for reversals,
            // so the reversal receipt carries the negated allocation against the same invoice.
            string reversalNumber = await numbers.NextAsync("ARREC", cancellationToken).ConfigureAwait(false);
            Guid reversalJournalId = await engine.PostAsync(
                    new FinancialEvent(
                        LegReversedEventType,
                        session.TenantId,
                        scopedTenant.StoreId,
                        _clock.UtcNow,
                        reversalNumber,
                        new Dictionary<string, Money>(StringComparer.Ordinal)
                        {
                            ["Principal"] = -allocation,
                        }),
                    cancellationToken)
                .ConfigureAwait(false);

            ArReceipt reversal = ArReceipt.Record(
                session.TenantId,
                scopedTenant.StoreId,
                PartnerId.From(session.CustomerGroupPartnerId ?? Guid.Empty),
                reversalNumber,
                _clock.UtcNow,
                -allocation,
                reversalJournalId,
                [(posted.InvoiceId, -allocation)]);

            companyDb.SalesReturns.Add(salesReturn);
            foreach (SalesReturnLine returnLine in salesReturn.Lines)
            {
                returnLine.AssignCompany(posted.CompanyId);
                companyDb.SalesReturnLines.Add(returnLine);
            }

            reversal.AssignCompany(posted.CompanyId);
            companyDb.ArReceipts.Add(reversal);
            foreach (ArReceiptAllocation allocationRow in reversal.Allocations)
            {
                allocationRow.AssignCompany(posted.CompanyId);
                companyDb.ArReceiptAllocations.Add(allocationRow);
            }

            CompanyOutboxCapture.Capture(
                companyDb, _replication, _replicas, _hybridClock, _node, _clock, salesReturn);
            CompanyOutboxCapture.Capture(
                companyDb, _replication, _replicas, _hybridClock, _node, _clock, reversal);

            audit.Stamp(companyDb);
            await companyDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            audit.Complete();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        // Holds the failed session took are released outside the money transaction: the
        // reservation service owns its own serialisable transactions (ADR-102).
        IReadOnlyList<Guid> holds = await FindOpenHoldsAsync(
                session, posted.CompanyId, cancellationToken)
            .ConfigureAwait(false);
        foreach (Guid held in holds)
        {
            await _gateway.RunReservationAsync(
                    session.TenantId, posted.CompanyId,
                    provider => provider.GetRequiredService<IReservationService>()
                        .ReleaseAsync(held, $"Trading session {session.SessionNumber} compensated", cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<Guid>> FindOpenHoldsAsync(
        TradingSession session, Guid companyId, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopes.CreateScope();
        Bind(scope, session.TenantId, companyId);

        await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(
                scope, CompanyAccessMode.Read, cancellationToken)
            .ConfigureAwait(false);

        // Chain-aware: a row's born-state stays Held forever, so openness is the LATEST row
        // per chain, not the row state. Releasing a chain that already closed would append a
        // bogus second terminal row (an 08c CloseOnce hole, recorded as a follow-up) — hence
        // the anti-join, not a bare state filter.
        return await companyDb.StockReservations
            .Where(reservation => reservation.Source == ReservationSource.MixedBasket
                && reservation.SourceDocumentId == session.Id
                && reservation.CompanyId == companyId
                && reservation.State == ReservationState.Held
                && !companyDb.StockReservations.Any(terminal =>
                    terminal.ReservationId == reservation.ReservationId
                    && terminal.State != ReservationState.Held))
            .Select(reservation => reservation.ReservationId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CompletedSegment>> ReplayAsync(
        SagaIntent existing,
        TradingSession session,
        CancellationToken cancellationToken)
    {
        if (existing.State == SagaIntentState.Completed
            || existing.Legs.All(leg => leg.State == SagaLegState.Acknowledged))
        {
            if (existing.State != SagaIntentState.Completed)
            {
                existing.Complete();
            }

            List<CompletedSegment> replayed = [];
            foreach (SagaLeg leg in existing.Legs.OrderBy(leg => leg.CompanyId))
            {
                CompletedSegment? posted = FindPosted(session, leg.CompanyId)
                    ?? await FindPostedInCompanyAsync(session, leg.CompanyId, cancellationToken)
                        .ConfigureAwait(false);
                if (posted is null)
                {
                    throw new TradingSessionConflictException(
                        existing.Id,
                        $"Session {session.SessionNumber} replays as complete but company {leg.CompanyId} has no invoice for it.");
                }

                replayed.Add(posted);
            }

            if (session.Status is not TradingSessionStatus.Completed)
            {
                session.MarkCompleting();
                session.MarkCompleted(
                    [.. replayed.Select(p => (p.CompanyId, p.SaleId, p.InvoiceId, p.InvoiceNumber))],
                    _clock.UtcNow);
            }

            await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);
            return replayed;
        }

        // An incomplete intent under the session's own key is resumed, not refused: the till
        // replays the same key after any crash, and a new key is not available — the key IS
        // the session. Legs are idempotent (deterministic ids + existence checks), so
        // resuming is safe by construction.
        List<TradingSessionSegment> segments = [.. session.Segments.Where(s => !s.IsRemoved && s.LiveLines.Any())];
        if (session.Status is TradingSessionStatus.Completing)
        {
            // Already running elsewhere would double-drive legs; the intent row is the mutex
            // only in the sense that one process owns this call. A second concurrent call
            // finds Completing and waits for the first attempt's outcome instead.
            throw new TradingSessionConflictException(
                existing.Id,
                $"Session {session.SessionNumber} is already completing. Retry the completion to read its outcome.");
        }

        session.MarkCompleting();
        await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

                Dictionary<Guid, Guid> resumedLocations = [];
        foreach (TradingSessionSegment segment in segments)
        {
            resumedLocations[segment.CompanyId] = await _gateway.ResolveDefaultLocationAsync(
                    session.TenantId, segment.CompanyId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await ExecuteAsync(existing, session, segments, resumedLocations, cancellationToken).ConfigureAwait(false);
    }

    private static CompletedSegment? FindPosted(TradingSession session, Guid companyId)
    {
        TradingSessionSegment? segment = session.Segments
            .FirstOrDefault(s => s.CompanyId == companyId && !s.IsRemoved);
        if (segment?.ResultingInvoiceId is { } invoiceId
            && segment.ResultingSaleId is { } saleId
            && segment.ResultingInvoiceNumber is { } invoiceNumber)
        {
            return new CompletedSegment(companyId, saleId, invoiceId, invoiceNumber);
        }

        return null;
    }

    private async Task<CompletedSegment?> FindPostedInCompanyAsync(
        TradingSession session, Guid companyId, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopes.CreateScope();
        Bind(scope, session.TenantId, companyId);

        await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(
                scope, CompanyAccessMode.Read, cancellationToken)
            .ConfigureAwait(false);

        Guid saleId = LegDocumentId(session.Id, companyId, "sale");
        Guid invoiceId = LegDocumentId(session.Id, companyId, "invoice");

        Sale? sale = await companyDb.Sales
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == saleId, cancellationToken)
            .ConfigureAwait(false);
        Invoice? invoice = await companyDb.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            .ConfigureAwait(false);

        if (sale is { Status: SaleStatus.Completed } && invoice is { Status: InvoiceStatus.Posted })
        {
            return new CompletedSegment(companyId, sale.Id, invoice.Id, invoice.InvoiceNumber);
        }

        return null;
    }

    private static TillSession OpenTill(
        VumaRetailDbContext companyDb, TradingSession session, Guid companyId, DateTimeOffset now)
    {
        // ADR-145: each segment settles in its own till session so cash-up splits per company.
        // A zero-float system session, owned by the cashier — the drawer it counts is the same
        // physical drawer, and the per-company accountability is what this session adds.
        TillSession till = TillSession.Open(
            session.TenantId,
            null,
            session.TerminalId,
            session.CashierUserId,
            Money.Zero(session.Currency),
            now);
        till.AssignCompany(companyId);
        companyDb.TillSessions.Add(till);
        return till;
    }

    private PostingRuleEngine LegEngine(
        VumaRetailDbContext companyDb, ITenantContext tenant, ILoggerFactory loggers)
        => new(
            new PostingRuleRepository(companyDb),
            new AccountingPeriodRepository(companyDb),
            new JournalRepository(companyDb),
            new DocumentNumberSequence(companyDb, tenant),
            _clock);

    private StockLedgerPoster LegStockPoster(
        VumaRetailDbContext companyDb, IServiceScope scope, ILoggerFactory loggers)
        => new(
            new StockBalanceRepository(companyDb),
            new StockLedgerRepository(companyDb),
            new FinancialInventoryValuationEventPublisher(
                LegEngine(
                    companyDb,
                    scope.ServiceProvider.GetRequiredService<ITenantContext>(),
                    loggers),
                loggers.CreateLogger<FinancialInventoryValuationEventPublisher>()),
            _clock);

    private static Domain.Pos.TenderType MapTender(string? tenderType)
        => Enum.TryParse<Domain.Pos.TenderType>(tenderType, ignoreCase: true, out Domain.Pos.TenderType parsed)
            ? parsed
            : Domain.Pos.TenderType.Cash;

    private static Guid LegDocumentId(Guid sessionId, Guid companyId, string purpose)
        // Deterministic ids live in TradingLegIds (shared with the return facade).
        => TradingLegIds.DocumentId(sessionId, companyId, purpose);

    private static void SetEntityId(object entity, Guid id)
    {
        // Entities mint their own ids at construction; legs need caller-minted ones for replay
        // safety. The property setter is private by design — this is the one place allowed to
        // override it, and only with the leg's deterministic id, never a random one.
        var property = entity.GetType().GetProperty("Id")
            ?? throw new InvalidOperationException($"Entity {entity.GetType().Name} has no Id.");
        property.SetValue(entity, id);
    }

    // The file's single company-context acquisition (MultiCompanyGuardTests counts textual
    // .CreateAsync occurrences per file): every leg — write or read — opens its one company
    // database here, in its own scope, never two in one place.
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
            throw new ArgumentException("A basket leg needs its tenant.", nameof(tenantId));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A basket leg needs its company.", nameof(companyId));
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
            throw new TradingSessionConflictException(
                Guid.Empty,
                "This session was already recorded. Retry with the same key to replay it.");
        }
    }

    private static string Serialise(TradingSession session)
        => JsonSerializer.Serialize(new
        {
            sessionId = session.Id,
            sessionNumber = session.SessionNumber,
            sessionCompany = session.SessionCompanyId,
            segments = session.Segments
                .Where(segment => !segment.IsRemoved)
                .Select(segment => new
                {
                    company = segment.CompanyId,
                    lines = segment.LiveLines.Count(),
                }).ToArray(),
        });
}

/// <summary>One leg's posted documents.</summary>
/// <param name="SaleId">The completed till sale.</param>
/// <param name="InvoiceId">The posted tax invoice.</param>
/// <param name="InvoiceNumber">The invoice's number in that company's <c>INV</c> series.</param>
internal sealed record LegDocuments(Guid SaleId, Guid InvoiceId, string InvoiceNumber);

/// <summary>The completion failed after validation; posted segments were compensated.</summary>
/// <param name="SessionId">The session.</param>
/// <param name="IntentId">The in-flight intent.</param>
/// <param name="Posted">The segments that posted before the failure (all compensated).</param>
/// <param name="Reason">Which leg failed and why.</param>
/// <param name="Inner">The leg failure.</param>
public sealed class TradingSessionFailedException(
    Guid SessionId, Guid IntentId, IReadOnlyList<CompletedSegment> Posted, string Reason, Exception? Inner = null)
    : InvalidOperationException(
        $"Trading session {SessionId} failed after posting {Posted.Count} segment(s): {Reason}", Inner);

/// <summary>One basket leg failed hard (the company, not the arithmetic).</summary>
/// <param name="CompanyId">The failed leg's company.</param>
/// <param name="LegId">The failed leg (empty for the single-segment fast path, which has none).</param>
/// <param name="Reason">Why it failed.</param>
/// <param name="Inner">The underlying failure.</param>
public sealed class TradingLegFailedException(Guid CompanyId, Guid LegId, string Reason, Exception? Inner = null)
    : InvalidOperationException($"Basket leg {LegId} in company {CompanyId} failed: {Reason}", Inner);

/// <summary>A completion key is already completing elsewhere, or a replay disagrees with stored results.</summary>
/// <param name="IntentId">The existing intent, or empty when it could not be loaded.</param>
/// <param name="Advice">What the caller should do.</param>
public sealed class TradingSessionConflictException(Guid IntentId, string Advice)
    : InvalidOperationException($"Trading session conflict on intent {IntentId}: {Advice}");
