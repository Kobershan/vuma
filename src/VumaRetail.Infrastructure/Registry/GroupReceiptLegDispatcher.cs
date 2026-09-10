using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Finance.Posting;
using VumaRetail.Infrastructure.Inventory;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Interceptors;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>
/// Stage 07c's leg-dispatch table (TASK-07C-004): routes saga legs by intent type and executes
/// each one inside its own company's database, one serialisable transaction per leg (ADR-116).
/// </summary>
/// <remarks>
/// 06d owns the saga mechanism (intent states, timeouts, alarms); 07c owns its leg types, so the
/// table lives here, not in the shared coordinator. The shape mirrors
/// <c>MixedBasketCompletionService</c> deliberately: child scope bound to the company, deterministic
/// document ids so a retried leg finds its rows instead of doubling them (§4.11), the journal
/// posted through a company-database-bound <c>PostingRuleEngine</c>, outbox capture on the leg's
/// own context, and an audit stamp. One company context per leg, never two at once.
/// </remarks>
public sealed class GroupReceiptLegDispatcher(
    IServiceScopeFactory scopes,
    IReplicationRegistry replication,
    IReplicaWriter replicas,
    IHybridClock hybridClock,
    INodeIdentity node,
    IClock clock,
    ILogger<GroupReceiptLegDispatcher> logger)
{
    /// <summary>The saga intent type for one allocation leg posting a receipt in its company.</summary>
    public const string AllocationIntentType = "group-receipt-allocation";

    /// <summary>The saga intent type for reversing every leg of a group receipt.</summary>
    public const string ReversalIntentType = "group-receipt-reversal";

    /// <summary>Own-company allocation leg: cash lands in this company's own bank.</summary>
    public const string AllocatedEventType = "group.receipt.allocated";

    /// <summary>
    /// Sister-company allocation leg: cash lands in the capturing company's bank, so this
    /// company books inter-company clearing against the bank owner instead of its own bank.
    /// A separate event type because posting rules answer per event type, not per role.
    /// </summary>
    public const string AllocatedSisterEventType = "group.receipt.allocated.sister";

    /// <summary>Mirror of <see cref="AllocatedEventType"/> for reversing legs.</summary>
    public const string ReversedEventType = "group.receipt.reversed";

    /// <summary>Mirror of <see cref="AllocatedSisterEventType"/> for reversing legs.</summary>
    public const string ReversedSisterEventType = "group.receipt.reversed.sister";

    /// <summary>Bank-side clearing leg: the bank owner books cash against clearing.</summary>
    public const string ClearingRaisedEventType = "inter-company.clearing.raised";

    /// <summary>Mirror of <see cref="ClearingRaisedEventType"/> for reversing legs.</summary>
    public const string ClearingReversedEventType = "inter-company.clearing.reversed";

    /// <summary>
    /// Executes one allocation leg: posts the receipt journal through the company's own rules,
    /// records the <c>ArReceipt</c> (targeted slices against invoices, or one on-account slice),
    /// and reduces each targeted invoice's outstanding balance. Idempotent: the receipt id is
    /// deterministic in <c>(groupReceiptId, allocationId)</c>, so a retried leg finds its own
    /// receipt and posts nothing.
    /// </summary>
    /// <returns>The company-side receipt id.</returns>
    public async Task<Guid> ExecuteReceiptLegAsync(
        GroupReceipt receipt,
        GroupReceiptAllocation allocation,
        Guid sagaIntentId,
        Guid? clearingIntentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(allocation);

        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, receipt.TenantId, allocation.CompanyId);

        await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(scope, cancellationToken)
            .ConfigureAwait(false);

        ITenantContext scopedTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        AuditStamper audit = scope.ServiceProvider.GetRequiredService<AuditStamper>();
        ILoggerFactory loggers = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        Guid receiptId = GroupReceiptLegIds.DocumentId(receipt.Id, allocation.Id, "receipt");

        ArReceipt? replayed = await companyDb.ArReceipts
            .FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken)
            .ConfigureAwait(false);
        if (replayed is not null)
        {
            logger.LogDebug(
                "Group receipt leg {AllocationId} already applied as receipt {ReceiptId}; replay creates nothing.",
                allocation.Id, receiptId);
            return replayed.Id;
        }

        bool sister = allocation.CompanyId != receipt.CapturingCompanyId;

        await using var transaction = await companyDb.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            List<(Guid? ArInvoiceId, Money Amount)> slices =
                await SliceAgainstInvoicesAsync(companyDb, allocation, cancellationToken)
                    .ConfigureAwait(false);

            var numbers = new DocumentNumberSequence(companyDb, scopedTenant);
            string receiptNumber = await numbers.NextAsync("ARREC", cancellationToken).ConfigureAwait(false);

            PostingRuleEngine engine = LegEngine(companyDb, scopedTenant, loggers);
            Guid journalId = await engine.PostAsync(
                    new FinancialEvent(
                        sister ? AllocatedSisterEventType : AllocatedEventType,
                        receipt.TenantId,
                        scopedTenant.StoreId,
                        clock.UtcNow,
                        receiptNumber,
                        new Dictionary<string, Money>(StringComparer.Ordinal)
                        {
                            ["Gross"] = allocation.Amount,
                        }),
                    cancellationToken)
                .ConfigureAwait(false);

            ArReceipt posted = ArReceipt.RecordFromGroup(
                receipt.TenantId,
                scopedTenant.StoreId,
                PartnerId.From(allocation.CustomerPartnerId ?? Guid.Empty),
                receiptNumber,
                receipt.CapturedAt,
                allocation.Amount,
                journalId,
                slices,
                receipt.Id,
                clearingIntentId ?? sagaIntentId,
                bankAccountId: sister ? null : receipt.BankAccountId);
            SetEntityId(posted, receiptId);

            posted.AssignCompany(allocation.CompanyId);
            companyDb.ArReceipts.Add(posted);
            foreach (ArReceiptAllocation row in posted.Allocations)
            {
                row.AssignCompany(allocation.CompanyId);
                companyDb.ArReceiptAllocations.Add(row);
            }

            CompanyOutboxCapture.Capture(
                companyDb, replication, replicas, hybridClock, node, clock, posted);

            audit.Stamp(companyDb);
            await companyDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            audit.Complete();

            logger.LogDebug(
                "Group receipt leg {AllocationId} posted receipt {ReceiptNumber} in company {CompanyId}.",
                allocation.Id, receiptNumber, allocation.CompanyId);

            return posted.Id;
        }
        catch (Exception failure)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new GroupReceiptLegException(allocation.CompanyId, allocation.Id, failure.Message, failure);
        }
    }

    /// <summary>
    /// Executes one clearing leg of an <c>InterCompanyClearingIntent</c>. The bank-owner side
    /// posts its cash-against-clearing journal here; the sister side was already posted by the
    /// receipt leg in the same transaction as its receipt (business rule 3), so executing it is
    /// a verification that the receipt stands, not a second posting.
    /// </summary>
    public async Task ExecuteClearingLegAsync(
        InterCompanyClearingIntent intent,
        InterCompanyClearingLeg leg,
        GroupReceipt receipt,
        GroupReceiptAllocation allocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(leg);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(allocation);

        if (leg.CompanyId == intent.FromCompanyId)
        {
            await PostBankSideClearingAsync(intent, leg, receipt, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Sister side: the receipt leg booked Dr clearing / Cr customer atomically with the
        // receipt. Confirm the receipt exists; the caller acks the leg on success.
        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, receipt.TenantId, leg.CompanyId);

        await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(scope, cancellationToken)
            .ConfigureAwait(false);

        Guid receiptId = GroupReceiptLegIds.DocumentId(receipt.Id, allocation.Id, "receipt");
        bool stands = await companyDb.ArReceipts
            .AsNoTracking()
            .AnyAsync(r => r.Id == receiptId, cancellationToken)
            .ConfigureAwait(false);
        if (!stands)
        {
            throw new GroupReceiptLegException(
                leg.CompanyId, leg.Id,
                $"Clearing leg {leg.Id} has no posted receipt to clear against; run the receipt leg first.");
        }
    }

    /// <summary>
    /// Executes one receipt-reversal leg: posts the mirror journal, records a negative-amount
    /// reversal receipt against the same invoices (or on-account slice), and reinstates each
    /// targeted invoice's outstanding balance. Never edits a posted journal (§7 rule 6).
    /// Idempotent on the deterministic reversal receipt id.
    /// </summary>
    /// <returns>The reversal receipt id.</returns>
    public async Task<Guid> ExecuteReceiptReversalLegAsync(
        GroupReceipt receipt,
        GroupReceiptAllocation allocation,
        Guid reversalIntentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(allocation);

        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, receipt.TenantId, allocation.CompanyId);

        await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(scope, cancellationToken)
            .ConfigureAwait(false);

        ITenantContext scopedTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        AuditStamper audit = scope.ServiceProvider.GetRequiredService<AuditStamper>();
        ILoggerFactory loggers = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        Guid reversalId = GroupReceiptLegIds.DocumentId(receipt.Id, allocation.Id, "receipt-reversal");

        ArReceipt? replayed = await companyDb.ArReceipts
            .FirstOrDefaultAsync(r => r.Id == reversalId, cancellationToken)
            .ConfigureAwait(false);
        if (replayed is not null)
        {
            return replayed.Id;
        }

        bool sister = allocation.CompanyId != receipt.CapturingCompanyId;

        await using var transaction = await companyDb.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ArReceipt original = await companyDb.ArReceipts
                    .Include(r => r.Allocations)
                    .FirstOrDefaultAsync(
                        r => r.Id == GroupReceiptLegIds.DocumentId(receipt.Id, allocation.Id, "receipt"),
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new GroupReceiptLegException(
                    allocation.CompanyId, allocation.Id,
                    $"Reversal leg has no posted receipt for allocation {allocation.Id}.");

            var numbers = new DocumentNumberSequence(companyDb, scopedTenant);
            string reversalNumber = await numbers.NextAsync("ARREC", cancellationToken).ConfigureAwait(false);

            PostingRuleEngine engine = LegEngine(companyDb, scopedTenant, loggers);
            Guid journalId = await engine.PostAsync(
                    new FinancialEvent(
                        sister ? ReversedSisterEventType : ReversedEventType,
                        receipt.TenantId,
                        scopedTenant.StoreId,
                        clock.UtcNow,
                        reversalNumber,
                        new Dictionary<string, Money>(StringComparer.Ordinal)
                        {
                            ["Gross"] = allocation.Amount,
                        }),
                    cancellationToken)
                .ConfigureAwait(false);

            List<(Guid? ArInvoiceId, Money Amount)> slices = [];
            foreach (ArReceiptAllocation slice in original.Allocations)
            {
                Money negated = -slice.Amount;
                slices.Add((slice.ArInvoiceId, negated));
                if (slice.ArInvoiceId is { } invoiceId)
                {
                    ArInvoice invoice = await companyDb.ArInvoices
                            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
                            .ConfigureAwait(false)
                        ?? throw new GroupReceiptLegException(
                            allocation.CompanyId, allocation.Id,
                            $"Reversal leg cannot reinstate missing invoice {invoiceId}.");
                    invoice.Reinstate(slice.Amount);
                }
            }

            Money negative = -allocation.Amount;
            ArReceipt reversal = ArReceipt.RecordFromGroup(
                receipt.TenantId,
                scopedTenant.StoreId,
                original.PartnerId,
                reversalNumber,
                clock.UtcNow,
                negative,
                journalId,
                slices,
                receipt.Id,
                reversalIntentId,
                bankAccountId: sister ? null : receipt.BankAccountId);
            SetEntityId(reversal, reversalId);

            reversal.AssignCompany(allocation.CompanyId);
            companyDb.ArReceipts.Add(reversal);
            foreach (ArReceiptAllocation row in reversal.Allocations)
            {
                row.AssignCompany(allocation.CompanyId);
                companyDb.ArReceiptAllocations.Add(row);
            }

            CompanyOutboxCapture.Capture(
                companyDb, replication, replicas, hybridClock, node, clock, reversal);

            audit.Stamp(companyDb);
            await companyDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            audit.Complete();

            return reversal.Id;
        }
        catch (GroupReceiptLegException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch (Exception failure)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new GroupReceiptLegException(allocation.CompanyId, allocation.Id, failure.Message, failure);
        }
    }

    /// <summary>
    /// Executes one clearing-reversal leg on the bank-owner side: posts the mirror of the
    /// cash-against-clearing journal. The sister side is mirrored by its receipt reversal.
    /// </summary>
    public async Task ExecuteClearingReversalLegAsync(
        InterCompanyClearingIntent intent,
        InterCompanyClearingLeg leg,
        Guid reversalIntentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(leg);

        if (leg.CompanyId != intent.FromCompanyId)
        {
            // Sister side: mirrored by the receipt-reversal leg in the same transaction.
            return;
        }

        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, intent.TenantId, leg.CompanyId);

        await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(scope, cancellationToken)
            .ConfigureAwait(false);

        ITenantContext scopedTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        AuditStamper audit = scope.ServiceProvider.GetRequiredService<AuditStamper>();
        ILoggerFactory loggers = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        string sourceReference = $"ICCLX-{intent.Id:N}-{leg.CompanyId:N}";
        bool posted = await companyDb.Journals
            .AsNoTracking()
            .AnyAsync(
                journal => journal.TenantId == intent.TenantId
                    && journal.SourceReference == sourceReference,
                cancellationToken)
            .ConfigureAwait(false);
        if (posted)
        {
            return;
        }

        await using var transaction = await companyDb.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            PostingRuleEngine engine = LegEngine(companyDb, scopedTenant, loggers);
            await engine.PostAsync(
                    new FinancialEvent(
                        ClearingReversedEventType,
                        intent.TenantId,
                        scopedTenant.StoreId,
                        clock.UtcNow,
                        sourceReference,
                        new Dictionary<string, Money>(StringComparer.Ordinal)
                        {
                            ["Gross"] = new Money(leg.Amount.Amount, leg.Currency),
                        }),
                    cancellationToken)
                .ConfigureAwait(false);

            audit.Stamp(companyDb);
            await companyDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            audit.Complete();
        }
        catch (Exception failure)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new GroupReceiptLegException(leg.CompanyId, leg.Id, failure.Message, failure);
        }
    }

    private async Task PostBankSideClearingAsync(
        InterCompanyClearingIntent intent,
        InterCompanyClearingLeg leg,
        GroupReceipt receipt,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, intent.TenantId, leg.CompanyId);

        await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(scope, cancellationToken)
            .ConfigureAwait(false);

        ITenantContext scopedTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        AuditStamper audit = scope.ServiceProvider.GetRequiredService<AuditStamper>();
        ILoggerFactory loggers = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        string sourceReference = $"ICCLR-{intent.Id:N}-{leg.CompanyId:N}";
        bool posted = await companyDb.Journals
            .AsNoTracking()
            .AnyAsync(
                journal => journal.TenantId == intent.TenantId
                    && journal.SourceReference == sourceReference,
                cancellationToken)
            .ConfigureAwait(false);
        if (posted)
        {
            logger.LogDebug(
                "Clearing leg {LegId} already posted; replay creates nothing.", leg.Id);
            return;
        }

        await using var transaction = await companyDb.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            PostingRuleEngine engine = LegEngine(companyDb, scopedTenant, loggers);
            await engine.PostAsync(
                    new FinancialEvent(
                        ClearingRaisedEventType,
                        intent.TenantId,
                        scopedTenant.StoreId,
                        receipt.CapturedAt,
                        sourceReference,
                        new Dictionary<string, Money>(StringComparer.Ordinal)
                        {
                            ["Gross"] = new Money(leg.Amount.Amount, leg.Currency),
                        }),
                    cancellationToken)
                .ConfigureAwait(false);

            audit.Stamp(companyDb);
            await companyDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            audit.Complete();

            logger.LogDebug(
                "Clearing leg {LegId} posted bank-side clearing in company {CompanyId}.",
                leg.Id, leg.CompanyId);
        }
        catch (Exception failure)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new GroupReceiptLegException(leg.CompanyId, leg.Id, failure.Message, failure);
        }
    }

    /// <summary>
    /// Fills the allocation amount from the listed invoices in order (business rule 5: an
    /// allocation can never over-allocate an invoice — the domain refuses with
    /// <c>OverAllocationException</c>). No targets means one on-account slice.
    /// </summary>
    private static async Task<List<(Guid? ArInvoiceId, Money Amount)>> SliceAgainstInvoicesAsync(
        VumaRetailDbContext companyDb,
        GroupReceiptAllocation allocation,
        CancellationToken cancellationToken)
    {
        List<(Guid? ArInvoiceId, Money Amount)> slices = [];

        if (allocation.TargetInvoiceIds.Count == 0)
        {
            slices.Add((null, allocation.Amount));
            return slices;
        }

        Money remaining = allocation.Amount;
        foreach (Guid invoiceId in allocation.TargetInvoiceIds)
        {
            if (remaining.IsZero)
            {
                break;
            }

            ArInvoice invoice = await companyDb.ArInvoices
                    .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new GroupReceiptLegException(
                    allocation.CompanyId, allocation.Id,
                    $"Allocation targets invoice {invoiceId}, which does not exist in company {allocation.CompanyId}.");

            if (invoice.Currency != allocation.Amount.Currency)
            {
                throw new GroupReceiptLegException(
                    allocation.CompanyId, allocation.Id,
                    $"Allocation targets invoice {invoiceId} in {invoice.Currency}, but the allocation is in {allocation.Amount.Currency}.");
            }

            Money slice = remaining.Amount <= invoice.OutstandingBalance.Amount
                ? remaining
                : invoice.OutstandingBalance;
            if (slice.Amount <= 0)
            {
                throw new GroupReceiptLegException(
                    allocation.CompanyId, allocation.Id,
                    $"Allocation targets invoice {invoiceId}, which has nothing outstanding.");
            }

            invoice.Allocate(slice);
            slices.Add((invoiceId, slice));
            remaining -= slice;
        }

        if (!remaining.IsZero)
        {
            throw new GroupReceiptLegException(
                allocation.CompanyId, allocation.Id,
                $"Allocation of {allocation.Amount.Amount:F2} exceeds the targeted invoices' outstanding balances.");
        }

        return slices;
    }

    private PostingRuleEngine LegEngine(
        VumaRetailDbContext companyDb, ITenantContext tenant, ILoggerFactory loggers)
        => new(
            new PostingRuleRepository(companyDb),
            new AccountingPeriodRepository(companyDb),
            new JournalRepository(companyDb),
            new DocumentNumberSequence(companyDb, tenant),
            clock);

    private static void Bind(IServiceScope scope, Guid tenantId, Guid companyId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A group receipt leg needs its tenant.", nameof(tenantId));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A group receipt leg needs its company.", nameof(companyId));
        }

        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);
    }

    // The file's single company-context acquisition (MultiCompanyGuardTests counts textual
    // .CreateAsync occurrences per file): every company-database leg in this dispatcher goes
    // through this one site, each in its own child scope, never two at once (ADR-116).
    private async Task<VumaRetailDbContext> OpenCompanyDbAsync(
        IServiceScope scope, CancellationToken cancellationToken)
    {
        ICompanyDbContextFactory companies = scope.ServiceProvider
            .GetRequiredService<ICompanyDbContextFactory>();
        return await companies.CreateAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void SetEntityId(object entity, Guid id)
    {
        // Entities mint their own ids at construction; legs need caller-minted deterministic
        // ids for replay safety. The property setter is private by design — this is the one
        // place allowed to override it, and only with the leg's deterministic id.
        var property = entity.GetType().GetProperty("Id")
            ?? throw new InvalidOperationException($"Entity {entity.GetType().Name} has no Id.");
        property.SetValue(entity, id);
    }
}

/// <summary>Deterministic document ids for group-receipt legs (Stage 07c).</summary>
/// <remarks>
/// Same group document and allocation mint the same id on every attempt, so a retried leg finds
/// its rows instead of doubling them — the §4.11 lesson, same shape as <c>TradingLegIds</c> with
/// a distinct namespace so the two can never collide.
/// </remarks>
internal static class GroupReceiptLegIds
{
    /// <summary>Mints the deterministic id for one leg document.</summary>
    public static Guid DocumentId(Guid groupDocumentId, Guid allocationId, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"vuma:group-receipt:{groupDocumentId:N}:{allocationId:N}:{purpose}"));
        byte[] guid = new byte[16];
        Array.Copy(hash, guid, 16);
        guid[6] = (byte)((guid[6] & 0x0F) | 0x50);
        guid[8] = (byte)((guid[8] & 0x3F) | 0x80);
        return new Guid(guid);
    }
}

/// <summary>A group-receipt leg failed inside its company's database. The registry rows stay
/// pending with this message; recovery is retry, never re-key (ADR-104).</summary>
public sealed class GroupReceiptLegException(Guid companyId, Guid legId, string reason, Exception? inner = null)
    : InvalidOperationException($"Group receipt leg {legId} in company {companyId} failed: {reason}", inner)
{
    /// <summary>The company whose leg failed.</summary>
    public Guid CompanyId { get; } = companyId;

    /// <summary>The allocation or clearing leg that failed.</summary>
    public Guid LegId { get; } = legId;

    /// <summary>Why the leg failed, without provider or connection details.</summary>
    public string Reason { get; } = reason;
}
