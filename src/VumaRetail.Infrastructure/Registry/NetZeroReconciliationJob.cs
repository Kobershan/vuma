using VumaRetail.Application.Abstractions.Registry;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>
/// Scheduled job that verifies clearing balances across all company databases net to zero.
/// Run after every intent and on a schedule, with an alarm and a named owner (ADR-105).
/// Not a transactional assertion — because there is no longer one to make.
/// </summary>
public sealed class NetZeroReconciliationJob
{
    private readonly IGroupReceiptRepository _repository;
    private readonly ICompanyFanOut _companyFanOut;
    private readonly IAlarmService _alarmService;

    public NetZeroReconciliationJob(
        IGroupReceiptRepository repository,
        ICompanyFanOut companyFanOut,
        IAlarmService alarmService)
    {
        _repository = repository;
        _companyFanOut = companyFanOut;
        _alarmService = alarmService;
    }

    /// <summary>
    /// Runs the net-zero reconciliation check across all company databases.
    /// Clears clearing accounts should sum to zero across the group.
    /// </summary>
    public async Task<ReconciliationResult> RunAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Get all outstanding clearing intents
        IReadOnlyList<InterCompanyClearingIntent> outstandingIntents =
            await _repository.GetOutstandingIntentsAsync(tenantId, cancellationToken);

        // Fan out to each company and get their clearing account balances
        IReadOnlyList<CompanyClearingBalance> balances =
            await _companyFanOut.GetClearingBalancesAsync(tenantId, cancellationToken);

        // Verify net-zero: sum of all clearing balances should be zero
        decimal totalDebit = balances.Sum(b => b.DebitAmount);
        decimal totalCredit = balances.Sum(b => b.CreditAmount);
        bool isBalanced = Math.Abs(totalDebit - totalCredit) < 0.01m;

        if (!isBalanced)
        {
            await _alarmService.RaiseAlarmAsync(
                tenantId,
                "ClearingNetZeroMismatch",
                $"Clearing balances do not net to zero. Debit: {totalDebit}, Credit: {totalCredit}. " +
                $"Outstanding intents: {outstandingIntents.Count}.",
                cancellationToken);
        }

        return new ReconciliationResult
        {
            IsBalanced = isBalanced,
            TotalDebit = totalDebit,
            TotalCredit = totalCredit,
            OutstandingIntents = outstandingIntents.Count,
            Balances = balances,
        };
    }
}

/// <summary>Result of a net-zero reconciliation check.</summary>
public sealed class ReconciliationResult
{
    public bool IsBalanced { get; init; }
    public decimal TotalDebit { get; init; }
    public decimal TotalCredit { get; init; }
    public int OutstandingIntents { get; init; }
    public IReadOnlyList<CompanyClearingBalance> Balances { get; init; } = [];
}

/// <summary>Clearing account balance from one company database.</summary>
public sealed class CompanyClearingBalance
{
    public Guid CompanyId { get; init; }
    public decimal DebitAmount { get; init; }
    public decimal CreditAmount { get; init; }
}

/// <summary>
/// Alarm service for operational alerts (unapplied legs, clearing mismatches).
/// </summary>
public interface IAlarmService
{
    Task RaiseAlarmAsync(Guid tenantId, string alarmType, string message, CancellationToken cancellationToken = default);
}
