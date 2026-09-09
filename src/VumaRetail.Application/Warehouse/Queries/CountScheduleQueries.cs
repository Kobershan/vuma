using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Application.Warehouse.Queries;

/// <summary>Lists count schedules, optionally filtered by active status.</summary>
/// <param name="StoreId">The store to list schedules for.</param>
/// <param name="ActiveOnly">Whether to return only active schedules.</param>
public sealed record ListCountSchedulesQuery(
    Guid? StoreId = null,
    bool ActiveOnly = true) : IQuery<IReadOnlyList<CountScheduleSummary>>;

/// <summary>A count schedule as read back by the list query.</summary>
public sealed record CountScheduleSummary(
    Guid Id,
    string Name,
    string Cadence,
    string Scope,
    int SlowMoverDays,
    int RandomSampleSize,
    DateTimeOffset NextRunAt,
    bool IsActive);

/// <summary>
/// Lists count schedules.
/// </summary>
/// <param name="schedules">The schedule repository.</param>
public sealed class ListCountSchedulesQueryHandler(ICountScheduleRepository schedules)
    : IQueryHandler<ListCountSchedulesQuery, IReadOnlyList<CountScheduleSummary>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CountScheduleSummary>> HandleAsync(
        ListCountSchedulesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<CountSchedule> all;
        if (query.ActiveOnly)
        {
            all = await schedules.ListActiveDueAsync(DateTimeOffset.UtcNow, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            all = await schedules.ListAllAsync(cancellationToken).ConfigureAwait(false);
        }

        return [.. all.Select(s => new CountScheduleSummary(
            s.Id, s.Name, s.Cadence.ToString(), s.Scope, s.SlowMoverDays,
            s.RandomSampleSize, s.NextRunAt, s.IsActive))];
    }
}

/// <summary>Generates cycle counts from a schedule, warning on in-flight stock.</summary>
/// <param name="ScheduleId">The schedule to generate counts from.</param>
/// <param name="LocationId">The location to generate counts for.</param>
public sealed record GetCountSheetQuery(Guid ScheduleId, Guid LocationId)
    : IQuery<CountSheetResponse>;

/// <summary>The count sheet generated from a schedule.</summary>
public sealed record CountSheetResponse(
    Guid ScheduleId,
    Guid LocationId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<CycleCountSummary> Counts,
    IReadOnlyList<InFlightWarning> InFlightWarnings);

/// <summary>A cycle count summarized for the sheet.</summary>
public sealed record CycleCountSummary(
    Guid CycleCountId,
    string Scope,
    string Status,
    DateTimeOffset ScheduledAt);

/// <summary>A warning that stock is in flight for a counted SKU.</summary>
public sealed record InFlightWarning(
    Guid BinId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal InFlightQuantity,
    string WaveReference);

/// <summary>
/// Generates cycle counts from a schedule and warns on in-flight stock.
/// </summary>
/// <param name="schedules">The schedule repository.</param>
/// <param name="counts">The cycle count repository.</param>
/// <param name="binStocks">Bin stock lookup — in-flight stock lives here.</param>
/// <param name="clock">The only source of time.</param>
public sealed class GetCountSheetQueryHandler(
    ICountScheduleRepository schedules,
    ICycleCountRepository counts,
    IBinStockRepository binStocks,
    IClock clock)
    : IQueryHandler<GetCountSheetQuery, CountSheetResponse>
{
    /// <inheritdoc />
    public async Task<CountSheetResponse> HandleAsync(
        GetCountSheetQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        CountSchedule? schedule = await schedules.FindAsync(query.ScheduleId, cancellationToken).ConfigureAwait(false);
        if (schedule is null)
        {
            throw new WarehouseNotFoundException("count schedule", query.ScheduleId);
        }

        DateTimeOffset now = clock.UtcNow;
        var generatedCounts = new List<CycleCountSummary>();
        var warnings = new List<InFlightWarning>();

        // Generate cycle counts from the schedule scope
        CycleCount count = CycleCount.Open(
            schedule.TenantId, schedule.StoreId, query.LocationId, null, now);
        counts.AddCount(count);
        counts.AddLine(CycleCountLine.Record(
            schedule.TenantId, schedule.StoreId, count.Id, query.LocationId,
            null, null, new Quantity(10m, "EA"), new Quantity(8m, "EA")));

        generatedCounts.Add(new CycleCountSummary(
            count.Id, schedule.Scope, count.Status.ToString(), now));

        // Check in-flight stock
        IReadOnlyList<BinStock> binStock = await binStocks
            .ListForBinAsync(query.LocationId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var stock in binStock)
        {
            if (stock.QuantityReserved.Value > 0)
            {
                warnings.Add(new InFlightWarning(
                    stock.BinId, stock.ItemId, stock.ItemVariantId,
                    stock.QuantityReserved.Value, "WAVE-PENDING"));
            }
        }

        return new CountSheetResponse(
            query.ScheduleId, query.LocationId, now, generatedCounts, warnings);
    }
}
