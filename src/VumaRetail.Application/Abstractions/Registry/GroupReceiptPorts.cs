using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

#pragma warning disable CS1591
#pragma warning disable CA1062

namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>
/// Repository for group receipt entities in the registry database (Stage 07c).
/// </summary>
public interface IGroupReceiptRepository
{
    Task<GroupReceipt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GroupReceipt>> GetUnallocatedAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task AddAsync(GroupReceipt receipt, CancellationToken cancellationToken = default);
    Task UpdateAsync(GroupReceipt receipt, CancellationToken cancellationToken = default);
    Task<GroupPaymentRun?> GetPaymentByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(GroupPaymentRun payment, CancellationToken cancellationToken = default);
    Task UpdateAsync(GroupPaymentRun payment, CancellationToken cancellationToken = default);
    Task<InterCompanyClearingIntent?> GetClearingIntentByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(InterCompanyClearingIntent intent, CancellationToken cancellationToken = default);
    Task UpdateAsync(InterCompanyClearingIntent intent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InterCompanyClearingIntent>> GetOutstandingIntentsAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InterCompanyClearingIntent>> GetSettledIntentsForDocumentAsync(Guid groupDocumentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Orchestrates group receipt capture, allocation, and reversal.
/// Lives in the registry; dispatches legs to company databases via ISagaCoordinator (ADR-104).
/// </summary>
public interface IGroupReceiptService
{
    /// <summary>Captures a group receipt. Posts nothing.</summary>
    Task<Guid> CaptureAsync(
        Guid tenantId, Guid capturingCompanyId, Guid bankAccountId,
        Money amount, string tenderType, string reference, DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocates part of a group receipt to one company. Dispatches a saga leg.
    /// Idempotent: retrying with the same allocation id posts exactly once (ADR-116).
    /// </summary>
    Task AllocateAsync(
        Guid tenantId, Guid groupReceiptId, Guid companyId,
        Guid? customerPartnerId, Money amount,
        IReadOnlyList<Guid>? targetInvoiceIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverses the entire group receipt. Dispatches reversing legs to every company involved.
    /// Never edits a posted journal (§7 rule 6).
    /// </summary>
    Task ReverseAsync(
        Guid tenantId, Guid groupReceiptId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Orchestrates group payment run capture, allocation, and reversal.
/// Same shape as <see cref="IGroupReceiptService"/> for the outbound direction (ADR-104).
/// </summary>
public interface IGroupPaymentService
{
    Task<Guid> CaptureAsync(
        Guid tenantId, Guid capturingCompanyId, Guid bankAccountId,
        Money amount, string tenderType, string reference, DateTimeOffset paidAt,
        CancellationToken cancellationToken = default);

    Task AllocateAsync(
        Guid tenantId, Guid groupPaymentRunId, Guid companyId,
        Guid? supplierPartnerId, Money amount,
        IReadOnlyList<Guid>? targetInvoiceIds,
        CancellationToken cancellationToken = default);

    Task ReverseAsync(
        Guid tenantId, Guid groupPaymentRunId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Assembles consolidated reports from each company's published period figures.
/// Read-only, labelled, carries watermark/AsAt, names stale contributors (ADR-106, ADR-119).
/// </summary>
public interface IConsolidationService
{
    Task<ConsolidatedTrialBalance> GetTrialBalanceAsync(
        Guid tenantId, DateOnly asOf, CancellationToken cancellationToken = default);

    Task<ConsolidatedIncomeStatement> GetIncomeStatementAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<ConsolidatedBalanceSheet> GetBalanceSheetAsync(
        Guid tenantId, DateOnly asOf, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UnappliedLegDto>> GetUnappliedLegsAsync(
        Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Consolidated trial balance assembled from multiple companies.</summary>
public sealed class ConsolidatedTrialBalance
{
    public string Watermark { get; init; } = "Consolidated — management information, not a statutory statement";
    public DateOnly AsOf { get; init; }
    public IReadOnlyList<ConsolidatedAccountLine> Accounts { get; init; } = [];
    public IReadOnlyList<CompanyContributorInfo> Contributors { get; init; } = [];
}

/// <summary>Consolidated income statement with inter-company eliminations.</summary>
public sealed class ConsolidatedIncomeStatement
{
    public string Watermark { get; init; } = "Consolidated — management information, not a statutory statement";
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public IReadOnlyList<ConsolidatedAccountLine> IncomeAccounts { get; init; } = [];
    public IReadOnlyList<ConsolidatedAccountLine> ExpenseAccounts { get; init; } = [];
    public Money NetIncome { get; init; }
    public IReadOnlyList<CompanyContributorInfo> Contributors { get; init; } = [];
}

/// <summary>Consolidated balance sheet with inter-company eliminations.</summary>
public sealed class ConsolidatedBalanceSheet
{
    public string Watermark { get; init; } = "Consolidated — management information, not a statutory statement";
    public DateOnly AsOf { get; init; }
    public IReadOnlyList<ConsolidatedAccountLine> AssetAccounts { get; init; } = [];
    public IReadOnlyList<ConsolidatedAccountLine> LiabilityAccounts { get; init; } = [];
    public IReadOnlyList<ConsolidatedAccountLine> EquityAccounts { get; init; } = [];
    public IReadOnlyList<CompanyContributorInfo> Contributors { get; init; } = [];
}

/// <summary>One account line in a consolidated report.</summary>
public sealed class ConsolidatedAccountLine
{
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public string AccountType { get; init; } = string.Empty;
    public Money Debit { get; init; }
    public Money Credit { get; init; }
}

/// <summary>Information about a company contributing to a consolidated report.</summary>
public sealed class CompanyContributorInfo
{
    public Guid CompanyId { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public DateTimeOffset AsAt { get; init; }
    public bool IsStale { get; init; }
    public string? StaleReason { get; init; }
}

/// <summary>One unapplied leg on the unapplied-legs report.</summary>
public sealed class UnappliedLegDto
{
    public Guid IntentId { get; init; }
    public string IntentType { get; init; } = string.Empty;
    public Guid CompanyId { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public Money Amount { get; init; }
    public string Direction { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public int AgeHours { get; init; }
    public string? LastError { get; init; }
}
