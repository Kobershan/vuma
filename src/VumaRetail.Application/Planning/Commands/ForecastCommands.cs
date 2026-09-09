using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Planning.Forecasting;
using VumaRetail.Domain.Planning;

namespace VumaRetail.Application.Planning.Commands;

/// <summary>Runs the nightly demand-history rollup over the trailing window.</summary>
/// <param name="WindowWeeks">How many trailing weeks to rebuild. Default 13.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RollupDemandHistoryCommand(int WindowWeeks = 13) : ICommand<RollupDemandHistoryOutcome>;

/// <summary>What a rollup pass did.</summary>
/// <param name="RowsUpserted">How many history rows were written or refreshed.</param>
/// <param name="Skus">How many distinct SKU/locations were touched.</param>
/// <param name="WindowStart">First day of the window.</param>
/// <param name="WindowEnd">Last day of the window.</param>
public sealed record RollupDemandHistoryOutcome(int RowsUpserted, int Skus, DateOnly WindowStart, DateOnly WindowEnd);

/// <summary>Rejects a malformed rollup command.</summary>
public sealed class RollupDemandHistoryCommandValidator : AbstractValidator<RollupDemandHistoryCommand>
{
    /// <summary>Builds the rules.</summary>
    public RollupDemandHistoryCommandValidator()
        => RuleFor(command => command.WindowWeeks).InclusiveBetween(1, 104);
}

/// <summary>
/// Aggregates sale issues into weekly Monday–Sunday buckets, idempotently. Re-running for the
/// same source data refreshes rows in place — totals never double. SKUs with any sale in the
/// window get explicit zero rows for their gap weeks.
/// </summary>
/// <param name="source">Sale issues out of the stock ledger.</param>
/// <param name="history">History insertion and lookup.</param>
/// <param name="tenant">The tenant the rows belong to.</param>
/// <param name="clock">The only source of time.</param>
public sealed class RollupDemandHistoryCommandHandler(
    IDemandHistorySource source,
    IDemandHistoryRepository history,
    ITenantContext tenant,
    IClock clock) : ICommandHandler<RollupDemandHistoryCommand, RollupDemandHistoryOutcome>
{
    /// <inheritdoc />
    public async Task<RollupDemandHistoryOutcome> HandleAsync(
        RollupDemandHistoryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        DateOnly windowStart = StartOfWeek(today.AddDays(-7 * (command.WindowWeeks - 1)));
        DateTimeOffset from = windowStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        DateTimeOffset to = clock.UtcNow;

        IReadOnlyList<SaleIssueRecord> issues = await source
            .ListSaleIssuesAsync(from, to, cancellationToken)
            .ConfigureAwait(false);

        var buckets = new Dictionary<(Guid Company, Guid Location, Guid? Item, Guid? Variant, DateOnly Week), (decimal Qty, string Uom)>();

        foreach (SaleIssueRecord issue in issues)
        {
            if (issue.Quantity >= 0m)
            {
                continue;
            }

            DateOnly week = StartOfWeek(DateOnly.FromDateTime(issue.OccurredAt.UtcDateTime));
            var key = (issue.CompanyId, issue.LocationId, issue.ItemId, issue.ItemVariantId, week);

            buckets.TryGetValue(key, out (decimal Qty, string Uom) existing);
            buckets[key] = (existing.Qty - issue.Quantity, string.IsNullOrWhiteSpace(existing.Uom) ? issue.Uom : existing.Uom);
        }

        // Gap weeks: every active SKU/location gets a zero row for every week of the window.
        var series = buckets.Keys
            .Select(key => (key.Company, key.Location, key.Item, key.Variant))
            .Distinct()
            .ToList();

        var weeks = new List<DateOnly>();

        for (int week = 0; week < command.WindowWeeks; week++)
        {
            weeks.Add(windowStart.AddDays(7 * week));
        }

        int rows = 0;

        foreach (var sku in series)
        {
            foreach (DateOnly week in weeks)
            {
                var key = (sku.Company, sku.Location, sku.Item, sku.Variant, week);
                buckets.TryGetValue(key, out (decimal Qty, string Uom) bucket);

                DemandHistory? existing = await history
                    .FindAsync(sku.Company, sku.Location, sku.Item, sku.Variant, week, cancellationToken)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    history.Add(DemandHistory.Create(
                        tenant.TenantId,
                        sku.Company,
                        sku.Location,
                        sku.Item,
                        sku.Variant,
                        week,
                        week.AddDays(6),
                        bucket.Qty,
                        string.IsNullOrWhiteSpace(bucket.Uom) ? "EA" : bucket.Uom,
                        clock.UtcNow));
                }
                else
                {
                    existing.Refresh(bucket.Qty, clock.UtcNow);
                }

                rows++;
            }
        }

        return new RollupDemandHistoryOutcome(
            rows, series.Count, windowStart, windowStart.AddDays(7 * command.WindowWeeks - 1));
    }

    internal static DateOnly StartOfWeek(DateOnly day)
    {
        // Monday-start weeks. DayOfWeek: Sunday = 0 … Saturday = 6.
        int offset = ((int)day.DayOfWeek + 6) % 7;

        return day.AddDays(-offset);
    }
}

/// <summary>Runs the configured forecast over every SKU/location with history.</summary>
/// <param name="HorizonWeeks">How many weeks ahead to forecast. Default 4.</param>
/// <param name="HistoryWeeks">How much history to feed each computation. Default 26.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record GenerateForecastsCommand(int HorizonWeeks = 4, int HistoryWeeks = 26)
    : ICommand<GenerateForecastsOutcome>;

/// <summary>What a forecast run did.</summary>
/// <param name="ForecastsWritten">How many snapshots were written.</param>
/// <param name="Skus">How many SKU/locations were forecast.</param>
/// <param name="ForecastPeriod">The first week forecast.</param>
public sealed record GenerateForecastsOutcome(int ForecastsWritten, int Skus, DateOnly ForecastPeriod);

/// <summary>Rejects a malformed forecast command.</summary>
public sealed class GenerateForecastsCommandValidator : AbstractValidator<GenerateForecastsCommand>
{
    /// <summary>Builds the rules.</summary>
    public GenerateForecastsCommandValidator()
    {
        RuleFor(command => command.HorizonWeeks).InclusiveBetween(1, 52);
        RuleFor(command => command.HistoryWeeks).InclusiveBetween(2, 104);
    }
}

/// <summary>
/// Forecasts every SKU/location with history, using its parameters' method where configured and
/// moving-average otherwise. The first run for a period writes <c>v1</c>; re-runs append <c>v(n+1)</c>.
/// </summary>
/// <param name="history">History reads.</param>
/// <param name="forecasts">Snapshot insertion and lookup.</param>
/// <param name="parameters">Per-SKU methods.</param>
/// <param name="engine">The pure forecast engine.</param>
/// <param name="tenant">The tenant the snapshots belong to.</param>
/// <param name="clock">The only source of time.</param>
/// <param name="principal">Who ran it, for the snapshot audit.</param>
public sealed class GenerateForecastsCommandHandler(
    IDemandHistoryRepository history,
    IDemandForecastRepository forecasts,
    IReplenishmentParameterRepository parameters,
    IForecastEngine engine,
    ITenantContext tenant,
    IClock clock,
    IPrincipalAccessor principal) : ICommandHandler<GenerateForecastsCommand, GenerateForecastsOutcome>
{
    /// <inheritdoc />
    public async Task<GenerateForecastsOutcome> HandleAsync(
        GenerateForecastsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        DateOnly windowStart = today.AddDays(-7 * command.HistoryWeeks);
        DateOnly forecastPeriod = RollupDemandHistoryCommandHandler.StartOfWeek(today).AddDays(7);

        IReadOnlyList<DemandHistory> window = await history
            .ListWindowAsync(windowStart, today, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ReplenishmentParameter> allParameters = await parameters
            .ListAllAsync(cancellationToken)
            .ConfigureAwait(false);

        var methods = allParameters.ToDictionary(
            parameter => (parameter.CompanyId!.Value, parameter.LocationId, parameter.ItemId, parameter.ItemVariantId),
            parameter => parameter.ForecastMethod);

        var groups = window
            .GroupBy(row => (row.CompanyId!.Value, row.LocationId, row.ItemId, row.ItemVariantId))
            .ToList();

        Guid? generatedBy = TryParseUser(principal);
        int written = 0;

        foreach (var group in groups)
        {
            var weeks = new List<decimal>();
            DateOnly cursor = RollupDemandHistoryCommandHandler.StartOfWeek(windowStart);

            var byWeek = group.ToDictionary(row => row.PeriodStart, row => row.TotalQuantity);

            while (cursor <= today)
            {
                weeks.Add(byWeek.TryGetValue(cursor, out decimal quantity) ? quantity : 0m);
                cursor = cursor.AddDays(7);
            }

            ForecastMethod method = methods.TryGetValue(group.Key, out ForecastMethod configured)
                ? configured
                : ForecastMethod.MovingAverage;

            ForecastResult result = engine.Compute(
                weeks, method, new ForecastOptions(command.HorizonWeeks));

            DemandForecast? latest = await forecasts
                .LatestAsync(group.Key.Item1, group.Key.LocationId, group.Key.ItemId, group.Key.ItemVariantId, forecastPeriod, cancellationToken)
                .ConfigureAwait(false);

            // One snapshot per horizon week, quantities flat across the horizon.
            for (int horizon = 0; horizon < command.HorizonWeeks; horizon++)
            {
                DateOnly period = forecastPeriod.AddDays(7 * horizon);

                DemandForecast? existing = horizon == 0
                    ? latest
                    : await forecasts
                        .LatestAsync(group.Key.Item1, group.Key.LocationId, group.Key.ItemId, group.Key.ItemVariantId, period, cancellationToken)
                        .ConfigureAwait(false);

                DemandForecast snapshot = existing is null
                    ? DemandForecast.CreateVersion1(
                        tenant.TenantId, group.Key.Item1,
                        group.Key.ItemId, group.Key.ItemVariantId, group.Key.LocationId,
                        period, method, result.Quantity, result.Mape, result.Bias,
                        clock.UtcNow, generatedBy)
                    : DemandForecast.CreateNextVersion(
                        existing, result.Quantity, result.Mape, result.Bias, clock.UtcNow, generatedBy);

                forecasts.Add(snapshot);
                written++;
            }
        }

        return new GenerateForecastsOutcome(written, groups.Count, forecastPeriod);
    }

    private static Guid? TryParseUser(IPrincipalAccessor principal)
    {
        const string prefix = "user:";

        return principal.Principal.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParse(principal.Principal[prefix.Length..], out Guid userId)
            ? userId
            : null;
    }
}
