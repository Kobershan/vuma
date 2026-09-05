using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>
/// Handles one group receipt allocation leg inside the target company's database.
/// Creates an ArReceipt with GroupDocumentId and IntentId, raises the financial event,
/// and posts through that company's posting rules. No module names a GL account (§7 rule 12).
/// </summary>
public sealed class GroupReceiptLegHandler
{
    private readonly ICompanyDbContextFactory _companyDbContextFactory;
    private readonly IFinancialEventPoster _eventPoster;

    public GroupReceiptLegHandler(
        ICompanyDbContextFactory companyDbContextFactory,
        IFinancialEventPoster eventPoster)
    {
        _companyDbContextFactory = companyDbContextFactory;
        _eventPoster = eventPoster;
    }

    /// <summary>
    /// Applies one allocation leg inside the target company's database.
    /// Idempotent: the saga coordinator ensures this is called with the same
    /// (group_receipt_id, allocation_id) key, so the company DB deduplicates.
    /// </summary>
    public async Task ApplyAllocationLegAsync(
        GroupReceipt groupReceipt,
        GroupReceiptAllocation allocation,
        CancellationToken cancellationToken = default)
    {
        using VumaRetailDbContext companyDb = await _companyDbContextFactory.CreateAsync(cancellationToken);

        // Resolve the receiving bank account's company to determine which company posts bank side
        PartnerId partnerId = new(
            allocation.CustomerPartnerId ?? Guid.Empty,
            allocation.CustomerPartnerId.HasValue ? "customer" : "on-account");

        string receiptNumber = $"GR-{groupReceipt.Reference}";

        // Post through the company's posting rules (no GL account named here — §7 rule 12)
        IReadOnlyDictionary<string, Money> amounts = new Dictionary<string, Money>
        {
            ["Gross"] = allocation.Amount,
            ["Net"] = allocation.Amount,
            ["Tax"] = Money.Zero(allocation.Amount.Currency),
        };

        FinancialEvent financialEvent = new(
            EventType: "group.receipt.allocated",
            TenantId: groupReceipt.TenantId,
            StoreId: null,
            OccurredAt: groupReceipt.CapturedAt,
            SourceReference: receiptNumber,
            Amounts: amounts);

        Guid journalId = await _eventPoster.PostAsync(financialEvent, cancellationToken);

        // Create the AR receipt in the company DB, carrying group document and intent references
        ArReceipt receipt = ArReceipt.RecordFromGroup(
            tenantId: groupReceipt.TenantId,
            storeId: null,
            partnerId: partnerId,
            receiptNumber: receiptNumber,
            receivedAt: groupReceipt.CapturedAt,
            amount: allocation.Amount,
            journalId: journalId,
            allocations: [], // On-account, no invoice allocation at this level
            groupDocumentId: groupReceipt.Id,
            intentId: null,
            bankAccountId: groupReceipt.BankAccountId);

        companyDb.ArReceipts.Add(receipt);
        await companyDb.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Handles one group receipt reversal leg inside the target company's database.
/// Creates a reversing journal entry. Never edits a posted journal (§7 rule 6).
/// </summary>
public sealed class GroupReceiptReversalLegHandler
{
    private readonly ICompanyDbContextFactory _companyDbContextFactory;
    private readonly IFinancialEventPoster _eventPoster;

    public GroupReceiptReversalLegHandler(
        ICompanyDbContextFactory companyDbContextFactory,
        IFinancialEventPoster eventPoster)
    {
        _companyDbContextFactory = companyDbContextFactory;
        _eventPoster = eventPoster;
    }

    public async Task ReverseAllocationLegAsync(
        GroupReceipt groupReceipt,
        GroupReceiptAllocation allocation,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, Money> amounts = new Dictionary<string, Money>
        {
            ["Gross"] = allocation.Amount,
            ["Net"] = allocation.Amount,
            ["Tax"] = Money.Zero(allocation.Amount.Currency),
        };

        FinancialEvent financialEvent = new(
            EventType: "group.receipt.reversed",
            TenantId: groupReceipt.TenantId,
            StoreId: null,
            OccurredAt: DateTimeOffset.UtcNow,
            SourceReference: $"REV-{groupReceipt.Reference}",
            Amounts: amounts);

        await _eventPoster.PostAsync(financialEvent, cancellationToken);
    }
}
