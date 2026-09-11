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
/// <param name="clock">The only source of time.</param>
public sealed class ListCountSchedulesQueryHandler(ICountScheduleRepository schedules, IClock clock)
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
            all = await schedules.ListActiveDueAsync(clock.UtcNow, cancellationToken)
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
/// <param name="bins">The active bins at the requested location.</param>
/// <param name="binStocks">Bin stock lookup — in-flight stock lives here.</param>
/// <param name="movements">Movement history used to select slow movers.</param>
/// <param name="clock">The only source of time.</param>
/// <param name="waves">Optional active-wave lookup used to defer bins currently being picked.</param>
public sealed class GetCountSheetQueryHandler(
    ICountScheduleRepository schedules,
    ICycleCountRepository counts,
    IBinRepository bins,
    IBinStockRepository binStocks,
    IBinStockMovementRepository movements,
    IClock clock,
    IPickWaveRepository? waves = null)
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

        IReadOnlyList<Bin> locationBins = await bins.ListActiveForLocationAsync(query.LocationId, cancellationToken)
            .ConfigureAwait(false);

        var stockRows = new List<(Bin Bin, BinStock Stock)>();
        foreach (Bin bin in locationBins)
        {
            IReadOnlyList<BinStock> stock = await binStocks.ListForBinAsync(bin.Id, cancellationToken)
                .ConfigureAwait(false);
            stockRows.AddRange(stock.Select(balance => (bin, balance)));
        }

        IReadOnlyList<StockMovementActivity> activity =
            await movements.ListLastActivityForLocationAsync(query.LocationId, cancellationToken)
                .ConfigureAwait(false);
        IReadOnlyList<PickTask> activePickingTasks = waves is null
            ? []
            : await waves.ListOpenPickingTasksAsync(query.LocationId, cancellationToken)
                .ConfigureAwait(false);
        HashSet<(Guid BinId, Guid? ItemId, Guid? ItemVariantId)> deferred = activePickingTasks
            .Where(task => task.AllocatedBinId is not null)
            .Select(task => (task.AllocatedBinId!.Value, task.ItemId, task.ItemVariantId))
            .ToHashSet();
        DateTimeOffset cutoff = now.AddDays(-schedule.SlowMoverDays);
        var activityBySku = activity.ToDictionary(
            item => (item.ItemId, item.ItemVariantId), item => item.LastMovedAt);

        // A schedule targets untouched/old stock first and adds a deterministic sample. If a new
        // deployment has no movement history yet, all stock is included so the first count is useful.
        List<(Bin Bin, BinStock Stock)> selected = activity.Count == 0
            ? stockRows.Where(row => !deferred.Contains((row.Bin.Id, row.Stock.ItemId, row.Stock.ItemVariantId))).ToList()
            : stockRows.Where(row => !activityBySku.TryGetValue(
                (row.Stock.ItemId, row.Stock.ItemVariantId), out DateTimeOffset lastMoved)
                || lastMoved <= cutoff)
                .Where(row => !deferred.Contains((row.Bin.Id, row.Stock.ItemId, row.Stock.ItemVariantId)))
                .OrderBy(row => activityBySku.TryGetValue(
                    (row.Stock.ItemId, row.Stock.ItemVariantId), out DateTimeOffset lastMoved)
                    ? lastMoved : DateTimeOffset.MinValue)
                .ToList();

        if (activity.Count > 0 && schedule.RandomSampleSize > 0)
        {
            selected.AddRange(stockRows
                .Where(row => !selected.Contains(row)
                    && !deferred.Contains((row.Bin.Id, row.Stock.ItemId, row.Stock.ItemVariantId)))
                .OrderBy(row => StableSampleKey(row.Stock.ItemId, row.Stock.ItemVariantId))
                .Take(schedule.RandomSampleSize));
        }

        // Generate one cycle count from the schedule scope and snapshot selected SKU stock.
        CycleCount count = CycleCount.Open(
            schedule.TenantId, schedule.StoreId, query.LocationId, null, now);
        counts.AddCount(count);

        foreach ((Bin bin, BinStock balance) in selected)
        {
            counts.AddLine(CycleCountLine.Record(
                schedule.TenantId, schedule.StoreId, count.Id, bin.Id,
                balance.ItemId, balance.ItemVariantId,
                balance.QuantityOnHand, balance.QuantityOnHand));
        }

        generatedCounts.Add(new CycleCountSummary(
            count.Id, schedule.Scope, count.Status.ToString(), now));

        // Stock in consolidation, packing and dispatch bins is still on hand but not available.
        foreach (Bin bin in locationBins)
        {
            if (bin.Type is not (BinType.Consolidation or BinType.Packing or BinType.Dispatch))
            {
                continue;
            }

            IReadOnlyList<BinStock> stock = await binStocks.ListForBinAsync(bin.Id, cancellationToken)
                .ConfigureAwait(false);
            foreach (BinStock balance in stock.Where(x => x.QuantityOnHand.Value > 0m))
            {
                warnings.Add(new InFlightWarning(
                    balance.BinId, balance.ItemId, balance.ItemVariantId,
                    balance.QuantityOnHand.Value, $"{bin.Code}:STAGING"));
            }
        }

        return new CountSheetResponse(
            query.ScheduleId, query.LocationId, now, generatedCounts, warnings);
    }

    private static int StableSampleKey(Guid? itemId, Guid? variantId)
        => HashCode.Combine(itemId ?? Guid.Empty, variantId ?? Guid.Empty);
}
