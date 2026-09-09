using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Planning.Forecasting;
using VumaRetail.Domain.Planning;

namespace VumaRetail.Application.Planning.Commands;

/// <summary>Creates or updates replenishment parameters for one SKU at one location.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record UpsertReplenishmentParametersCommand(
    Guid CompanyId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    ForecastMethod ForecastMethod,
    decimal ServiceLevelPercent,
    int LeadTimeDays,
    int ReviewPeriodDays,
    string Uom) : ICommand<Guid>;

/// <summary>Rejects malformed parameters.</summary>
public sealed class UpsertReplenishmentParametersCommandValidator
    : AbstractValidator<UpsertReplenishmentParametersCommand>
{
    /// <summary>Builds the rules.</summary>
    public UpsertReplenishmentParametersCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
        RuleFor(command => command.LocationId).NotEmpty();
        RuleFor(command => command).Must(HaveExactlyOneSku).WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
        RuleFor(command => command.ForecastMethod).IsInEnum();
        RuleFor(command => command.ServiceLevelPercent).ExclusiveBetween(0m, 100m);
        RuleFor(command => command.LeadTimeDays).GreaterThanOrEqualTo(0);
        RuleFor(command => command.ReviewPeriodDays).GreaterThanOrEqualTo(1);
        RuleFor(command => command.Uom).NotEmpty().MaximumLength(16);
    }

    private static bool HaveExactlyOneSku(UpsertReplenishmentParametersCommand command)
        => (command.ItemId is null) != (command.ItemVariantId is null);
}

/// <summary>Upserts the parameters.</summary>
public sealed class UpsertReplenishmentParametersCommandHandler(
    IReplenishmentParameterRepository parameters,
    ITenantContext tenant) : ICommandHandler<UpsertReplenishmentParametersCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        UpsertReplenishmentParametersCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ReplenishmentParameter? existing = await parameters
            .FindAsync(command.CompanyId, command.LocationId, command.ItemId, command.ItemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            var created = ReplenishmentParameter.Create(
                tenant.TenantId,
                command.CompanyId,
                command.LocationId,
                command.ItemId,
                command.ItemVariantId,
                command.ForecastMethod,
                command.ServiceLevelPercent,
                command.LeadTimeDays,
                command.ReviewPeriodDays,
                command.Uom);

            parameters.Add(created);

            return created.Id;
        }

        existing.Update(
            command.ForecastMethod,
            command.ServiceLevelPercent,
            command.LeadTimeDays,
            command.ReviewPeriodDays,
            command.Uom);

        return existing.Id;
    }
}

/// <summary>Recalculates safety stock and reorder points for every parameterised SKU.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record RefreshSafetyStockCommand : ICommand<RefreshSafetyStockOutcome>;

/// <summary>What a safety-stock refresh did.</summary>
/// <param name="Calculated">How many calculations were recorded.</param>
/// <param name="LowConfidence">How many used the fallback.</param>
/// <param name="Skipped">How many SKUs had no history at all.</param>
public sealed record RefreshSafetyStockOutcome(int Calculated, int LowConfidence, int Skipped);

/// <summary>
/// Runs the safety-stock calculator over every parameter row. SKUs whose forecast horizon cannot
/// cover lead time plus review period are refused loudly per SKU (recorded as skipped with the
/// reason in the outcome count, never silently zeroed).
/// </summary>
public sealed class RefreshSafetyStockCommandHandler(
    IReplenishmentParameterRepository parameters,
    IDemandHistoryRepository history,
    ISafetyStockCalculationRepository calculations,
    ISafetyStockCalculator calculator,
    IClock clock) : ICommandHandler<RefreshSafetyStockCommand, RefreshSafetyStockOutcome>
{
    /// <inheritdoc />
    public async Task<RefreshSafetyStockOutcome> HandleAsync(
        RefreshSafetyStockCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        IReadOnlyList<ReplenishmentParameter> all = await parameters
            .ListAllAsync(cancellationToken)
            .ConfigureAwait(false);

        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        int calculated = 0;
        int lowConfidence = 0;
        int skipped = 0;

        foreach (ReplenishmentParameter parameter in all)
        {
            DateOnly from = today.AddDays(-7 * 26);
            IReadOnlyList<DemandHistory> rows = await history
                .ListSeriesAsync(
                    parameter.CompanyId!.Value, parameter.LocationId,
                    parameter.ItemId, parameter.ItemVariantId, from, today, cancellationToken)
                .ConfigureAwait(false);

            if (rows.Count == 0)
            {
                skipped++;
                continue;
            }

            var weeks = new List<decimal>();
            DateOnly cursor = RollupDemandHistoryCommandHandler.StartOfWeek(from);
            var byWeek = rows.ToDictionary(row => row.PeriodStart, row => row.TotalQuantity);

            while (cursor <= today)
            {
                weeks.Add(byWeek.TryGetValue(cursor, out decimal quantity) ? quantity : 0m);
                cursor = cursor.AddDays(7);
            }

            SafetyStockOutcome outcome;

            try
            {
                outcome = calculator.Calculate(new SafetyStockInputs(
                    weeks,
                    parameter.ServiceLevelPercent,
                    parameter.LeadTimeDays,
                    parameter.ReviewPeriodDays,
                    weeks.Count * 7));
            }
            catch (PlanningRuleException)
            {
                skipped++;
                continue;
            }

            calculations.Add(SafetyStockCalculation.Create(
                parameter.TenantId,
                parameter.CompanyId!.Value,
                parameter.LocationId,
                parameter.ItemId,
                parameter.ItemVariantId,
                outcome.LeadTimeDemandMean,
                outcome.DemandVariance,
                parameter.ServiceLevelPercent,
                outcome.HistoryWeeks,
                parameter.LeadTimeDays,
                outcome.SafetyStock,
                outcome.ReorderPoint,
                outcome.LowConfidence,
                outcome.Method,
                clock.UtcNow));

            calculated++;

            if (outcome.LowConfidence)
            {
                lowConfidence++;
            }
        }

        return new RefreshSafetyStockOutcome(calculated, lowConfidence, skipped);
    }
}

/// <summary>Runs ABC/XYZ classification over a trailing window.</summary>
/// <param name="WindowWeeks">The classification window. Default 12.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RunClassificationCommand(int WindowWeeks = 12) : ICommand<RunClassificationOutcome>;

/// <summary>What a classification run did.</summary>
/// <param name="SnapshotsWritten">How many snapshot rows were written.</param>
/// <param name="WindowStart">First day of the window.</param>
/// <param name="WindowEnd">Last day of the window.</param>
public sealed record RunClassificationOutcome(int SnapshotsWritten, DateOnly WindowStart, DateOnly WindowEnd);

/// <summary>Rejects a malformed classification command.</summary>
public sealed class RunClassificationCommandValidator : AbstractValidator<RunClassificationCommand>
{
    /// <summary>Builds the rules.</summary>
    public RunClassificationCommandValidator()
        => RuleFor(command => command.WindowWeeks).InclusiveBetween(4, 52);
}

/// <summary>
/// Snapshots ABC by demand-quantity share (A ≈ 80%, B ≈ next 15%, C the tail) and XYZ by
/// coefficient of variation (X ≤ 0.5, Y ≤ 1.0, Z above). Append-only: existing snapshots never
/// change when new sales arrive.
/// </summary>
public sealed class RunClassificationCommandHandler(
    IDemandHistoryRepository history,
    IAbcXyzClassificationRepository classifications,
    IClock clock) : ICommandHandler<RunClassificationCommand, RunClassificationOutcome>
{
    /// <inheritdoc />
    public async Task<RunClassificationOutcome> HandleAsync(
        RunClassificationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        DateOnly windowStart = today.AddDays(-7 * command.WindowWeeks);

        IReadOnlyList<DemandHistory> rows = await history
            .ListWindowAsync(windowStart, today, cancellationToken)
            .ConfigureAwait(false);

        var groups = rows
            .Where(row => row.CompanyId is not null)
            .GroupBy(row => (row.TenantId, row.CompanyId!.Value, row.LocationId, row.ItemId, row.ItemVariantId))
            .Select(group => new
            {
                Key = group.Key,
                Total = group.Sum(row => row.TotalQuantity),
                Series = WeeklySeries(group.ToList(), windowStart, today),
            })
            .OrderByDescending(entry => entry.Total)
            .ToList();

        decimal grandTotal = groups.Sum(entry => entry.Total);
        decimal running = 0m;
        int written = 0;

        foreach (var entry in groups)
        {
            running += entry.Total;
            decimal share = grandTotal == 0m ? 0m : entry.Total / grandTotal;
            decimal cumulative = grandTotal == 0m ? 0m : running / grandTotal;

            AbcClass abc = cumulative <= 0.8m ? AbcClass.A : cumulative <= 0.95m ? AbcClass.B : AbcClass.C;
            decimal cv = ForecastMath.CoefficientOfVariation(entry.Series);
            XyzClass xyz = cv <= 0.5m ? XyzClass.X : cv <= 1.0m ? XyzClass.Y : XyzClass.Z;

            classifications.Add(AbcXyzClassification.Create(
                entry.Key.TenantId,
                entry.Key.Value,
                entry.Key.LocationId,
                entry.Key.ItemId,
                entry.Key.ItemVariantId,
                abc,
                xyz,
                share,
                cv,
                windowStart,
                today,
                clock.UtcNow));

            written++;
        }

        return new RunClassificationOutcome(written, windowStart, today);
    }

    private static IReadOnlyList<decimal> WeeklySeries(
        IReadOnlyList<DemandHistory> rows, DateOnly windowStart, DateOnly today)
    {
        var byWeek = rows.ToDictionary(row => row.PeriodStart, row => row.TotalQuantity);
        var series = new List<decimal>();
        DateOnly cursor = RollupDemandHistoryCommandHandler.StartOfWeek(windowStart);

        while (cursor <= today)
        {
            series.Add(byWeek.TryGetValue(cursor, out decimal quantity) ? quantity : 0m);
            cursor = cursor.AddDays(7);
        }

        return series;
    }
}

/// <summary>Sets (or re-plans) a month's open-to-buy budget.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record SetOpenToBuyBudgetCommand(
    Guid CompanyId,
    int Year,
    int Month,
    string? CategoryCode,
    decimal PlannedAmount,
    string Currency) : ICommand<Guid>;

/// <summary>Rejects a malformed budget command.</summary>
public sealed class SetOpenToBuyBudgetCommandValidator : AbstractValidator<SetOpenToBuyBudgetCommand>
{
    /// <summary>Builds the rules.</summary>
    public SetOpenToBuyBudgetCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
        RuleFor(command => command.Year).InclusiveBetween(2000, 2100);
        RuleFor(command => command.Month).InclusiveBetween(1, 12);
        RuleFor(command => command.PlannedAmount).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.Currency).NotEmpty().MaximumLength(3);
    }
}

/// <summary>Sets the budget.</summary>
public sealed class SetOpenToBuyBudgetCommandHandler(
    IOpenToBuyBudgetRepository budgets,
    ITenantContext tenant) : ICommandHandler<SetOpenToBuyBudgetCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        SetOpenToBuyBudgetCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        OpenToBuyBudget? existing = await budgets
            .FindAsync(command.CompanyId, command.Year, command.Month, command.CategoryCode, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            var created = OpenToBuyBudget.Create(
                tenant.TenantId,
                command.CompanyId,
                command.Year,
                command.Month,
                command.CategoryCode,
                command.PlannedAmount,
                command.Currency);

            budgets.Add(created);

            return created.Id;
        }

        existing.Replan(command.PlannedAmount);

        return existing.Id;
    }
}
