using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;
using VumaRetail.Finance.Periods;

namespace VumaRetail.Finance.Commands;

/// <summary>Opens a new accounting period.</summary>
/// <param name="PeriodStart">The first day, inclusive.</param>
/// <param name="PeriodEnd">The last day, inclusive.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record OpenAccountingPeriodCommand(DateOnly PeriodStart, DateOnly PeriodEnd) : ICommand<Guid>;

/// <summary>Validates <see cref="OpenAccountingPeriodCommand"/>.</summary>
public sealed class OpenAccountingPeriodCommandValidator : AbstractValidator<OpenAccountingPeriodCommand>
{
    /// <summary>Builds the rules.</summary>
    public OpenAccountingPeriodCommandValidator()
        => RuleFor(command => command.PeriodEnd)
            .GreaterThanOrEqualTo(command => command.PeriodStart)
            .WithMessage("A period cannot end before it starts.");
}

/// <summary>Handles <see cref="OpenAccountingPeriodCommand"/>.</summary>
/// <param name="periods">The tenant's accounting calendar.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class OpenAccountingPeriodCommandHandler(IAccountingPeriodRepository periods, ITenantContext tenant)
    : ICommandHandler<OpenAccountingPeriodCommand, Guid>
{
    /// <inheritdoc />
    public Task<Guid> HandleAsync(OpenAccountingPeriodCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        AccountingPeriod period = AccountingPeriod.Open(tenant.TenantId, command.PeriodStart, command.PeriodEnd);
        periods.Add(period);
        return Task.FromResult(period.Id);
    }
}

/// <summary>
/// Closes an accounting period, refusing if any control account disagrees with its sub-ledger
/// (ADR-016).
/// </summary>
/// <param name="AccountingPeriodId">The period to close.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ClosePeriodCommand(Guid AccountingPeriodId) : ICommand;

/// <summary>Handles <see cref="ClosePeriodCommand"/>.</summary>
/// <param name="periods">The tenant's accounting calendar.</param>
/// <param name="checker">The control-account variance check.</param>
/// <param name="principal">Who is acting.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ClosePeriodCommandHandler(
    IAccountingPeriodRepository periods,
    PeriodVarianceChecker checker,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<ClosePeriodCommand, Unit>
{
    /// <inheritdoc />
    /// <exception cref="FinanceDocumentNotFoundException">The period does not exist.</exception>
    /// <exception cref="PeriodCloseBlockedException">A control account disagrees with its sub-ledger.</exception>
    public async Task<Unit> HandleAsync(ClosePeriodCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        AccountingPeriod period = await periods.FindByIdAsync(command.AccountingPeriodId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new FinanceDocumentNotFoundException(nameof(AccountingPeriod), command.AccountingPeriodId);

        IReadOnlyList<ControlAccountVariance> variances = await checker
            .CheckAsync(period, cancellationToken).ConfigureAwait(false);

        List<ControlAccountVariance> nonZero = [.. variances.Where(v => v.Variance != 0m)];

        if (nonZero.Count > 0)
        {
            throw new PeriodCloseBlockedException(nonZero);
        }

        period.Close(principal.Principal, clock.UtcNow);

        return Unit.Value;
    }
}

/// <summary>
/// Runs the ADR-016 daily control-account variance check across every open period, recording the
/// result whether or not there is a variance.
/// </summary>
/// <remarks>
/// The "automated daily job" ADR-016 calls for (ADR-063). Raised by
/// <c>FinanceReconciliationHostedService</c> once every 24 hours; also safe to run on demand.
/// </remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed record RunDailyReconciliationCheckCommand : ICommand;

/// <summary>Handles <see cref="RunDailyReconciliationCheckCommand"/>.</summary>
/// <param name="periods">The tenant's accounting calendar.</param>
/// <param name="checker">The control-account variance check.</param>
/// <param name="flags">Where a day's check is recorded.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="clock">The only source of time.</param>
public sealed class RunDailyReconciliationCheckCommandHandler(
    IAccountingPeriodRepository periods,
    PeriodVarianceChecker checker,
    IReconciliationVarianceFlagRepository flags,
    ITenantContext tenant,
    IClock clock) : ICommandHandler<RunDailyReconciliationCheckCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        RunDailyReconciliationCheckCommand command, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AccountingPeriod> openPeriods = await periods
            .ListOpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (AccountingPeriod period in openPeriods)
        {
            IReadOnlyList<ControlAccountVariance> variances = await checker
                .CheckAsync(period, cancellationToken).ConfigureAwait(false);

            foreach (ControlAccountVariance variance in variances)
            {
                flags.Add(ReconciliationVarianceFlag.Record(
                    tenant.TenantId,
                    period.Id,
                    variance.AccountId,
                    variance.ControlAccountType,
                    variance.GlBalance,
                    variance.SubLedgerBalance,
                    clock.UtcNow));
            }
        }

        return Unit.Value;
    }
}
