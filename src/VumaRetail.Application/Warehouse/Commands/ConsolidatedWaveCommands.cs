using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Warehouse;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Application.Warehouse.Commands;

/// <summary>Filter for building a consolidated pick wave (Stage 13b).</summary>
public sealed record PickWaveFilter(
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string GeographyLevel,      // "Province" | "City" | "Suburb"
    string GeographyValue,
    Guid? CompanyScope = null,
    Guid? LocationId = null,
    string? OrderStatus = null);

/// <summary>Builds a consolidated pick wave by grouping open order lines across orders (Stage 13b).</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record BuildConsolidatedWaveCommand(
    PickWaveFilter Filter,
    IReadOnlyList<OrderLineSummary> OrderLines) : ICommand<Guid>;

/// <summary>Validates <see cref="BuildConsolidatedWaveCommand"/>.</summary>
public sealed class BuildConsolidatedWaveCommandValidator : AbstractValidator<BuildConsolidatedWaveCommand>
{
    /// <summary>Builds the rules.</summary>
    public BuildConsolidatedWaveCommandValidator()
    {
        RuleFor(c => c.Filter.PeriodFrom).NotEmpty();
        RuleFor(c => c.Filter.PeriodTo).NotEmpty().GreaterThanOrEqualTo(c => c.Filter.PeriodFrom);
        RuleFor(c => c.Filter.GeographyLevel).NotEmpty().Must(l => l is "Province" or "City" or "Suburb");
        RuleFor(c => c.Filter.GeographyValue).NotEmpty();
        RuleFor(c => c.Filter.LocationId).NotEmpty();
    }
}

/// <summary>Builds a consolidated wave: groups order lines by (item, variant, uom, pack size).</summary>
/// <param name="waves">Wave insertion.</param>
/// <param name="breakdowns">Per-order contribution persistence.</param>
/// <param name="skus">Resolves pack size for grouping.</param>
/// <param name="tenant">The ambient tenant and store.</param>
/// <param name="orderLineReader">Loads qualifying order lines for API-originated builds.</param>
public sealed class BuildConsolidatedWaveCommandHandler(
    IPickWaveRepository waves,
    IPickWaveLineBreakdownRepository breakdowns,
    IPackSizeResolver skus,
    ITenantContext tenant,
    IOrderLineReader? orderLineReader = null)
    : ICommandHandler<BuildConsolidatedWaveCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(BuildConsolidatedWaveCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PickWaveFilter filter = command.Filter;

        IReadOnlyList<OrderLineSummary> sourceLines = command.OrderLines.Count > 0
            ? command.OrderLines
            : orderLineReader is null
                ? []
                : await orderLineReader.ReadOpenLinesAsync(
                    filter.LocationId!.Value, filter.PeriodFrom, filter.PeriodTo,
                    filter.GeographyLevel, filter.GeographyValue, filter.CompanyScope,
                    cancellationToken).ConfigureAwait(false);

        if (sourceLines.Count == 0)
        {
            throw new InvalidOperationException("No qualifying open order lines were found for this wave.");
        }

        // Group by (item, variant, uom, pack size)
        var grouped = await Task.WhenAll(sourceLines
            .Select(async line =>
            {
                PackSizeSnapshot packSize = await skus.ResolveAsync(
                    line.ItemId, line.ItemVariantId, line.UnitOfMeasure,
                    line.Quantity, cancellationToken)
                    .ConfigureAwait(false);

                return new
                {
                    line.ItemId,
                    line.ItemVariantId,
                    line.UnitOfMeasure,
                    packSize = packSize.Description,
                    Quantity = new Quantity(line.Quantity, line.UnitOfMeasure),
                    line.OrderId,
                    line.OrderLineId,
                    line.GeographyValue
                };
            }));

        var groupedByKey = grouped
            .GroupBy(g => new { g.ItemId, g.ItemVariantId, g.UnitOfMeasure, g.packSize })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.ItemVariantId,
                g.Key.UnitOfMeasure,
                PackSize = g.Key.packSize,
                TotalQuantity = g.Sum(x => x.Quantity.Value),
                Contributions = g.Select(x => new OrderLineSummary(
                    x.OrderId, x.OrderLineId, x.ItemId, x.ItemVariantId,
                    x.Quantity.Value, x.UnitOfMeasure, x.packSize, x.GeographyValue)).ToList()
            })
            .ToList();

        // The ambient tenant owns the wave. Order ids are references, never tenant identifiers.
        PickWave wave = PickWave.OpenConsolidated(
            tenant.TenantId, tenant.StoreId, filter.LocationId!.Value,
            filter.GeographyLevel, filter.GeographyValue,
            filter.PeriodFrom, filter.PeriodTo, filter.CompanyScope);

        waves.AddWave(wave);

        foreach (var group in groupedByKey)
        {
            PickTask task = PickTask.Create(
                wave.TenantId, wave.StoreId, wave.Id, group.ItemId, group.ItemVariantId,
                new Quantity(group.TotalQuantity, group.UnitOfMeasure), filter.CompanyScope?.ToString() ?? "consolidated");

            waves.AddTask(task);
            foreach (OrderLineSummary contribution in group.Contributions)
            {
                breakdowns.Add(PickWaveLineBreakdown.Create(
                    tenant.TenantId, tenant.StoreId, task.Id,
                    contribution.OrderId, contribution.OrderLineId, contribution.Quantity));
            }
        }

        return wave.Id;
    }
}

/// <summary>Releases a consolidated wave for picking (Stage 13b).</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record ReleaseConsolidatedWaveCommand(Guid PickWaveId) : ICommand;

/// <summary>Validates <see cref="ReleaseConsolidatedWaveCommand"/>.</summary>
public sealed class ReleaseConsolidatedWaveCommandValidator : AbstractValidator<ReleaseConsolidatedWaveCommand>
{
    /// <summary>Builds the rules.</summary>
    public ReleaseConsolidatedWaveCommandValidator() => RuleFor(c => c.PickWaveId).NotEmpty();
}

/// <summary>Releases a consolidated wave: allocates every line via the standard allocator (Stage 13b).</summary>
public sealed class ReleaseConsolidatedWaveCommandHandler(
    IPickWaveRepository waves,
    IBinStockRepository binStocks,
    IPickAllocationStrategy allocator,
    IClock clock)
    : ICommandHandler<ReleaseConsolidatedWaveCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(ReleaseConsolidatedWaveCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PickWave wave = await waves.FindAsync(command.PickWaveId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("pick wave", command.PickWaveId);

        if (wave.Status != PickWaveStatus.Open)
        {
            throw WarehouseRuleException.PickWaveWrongStatus(PickWaveStatus.Open, wave.Status);
        }

        IReadOnlyList<PickTask> tasks = await waves.ListTasksAsync(wave.Id, cancellationToken).ConfigureAwait(false);

        // Same reservation tracking as ReleasePickWaveCommandHandler
        Dictionary<(Guid BinId, Guid? ItemId, Guid? ItemVariantId), Quantity> reservedThisRelease = [];

        foreach (PickTask task in tasks.Where(task => task.Status == PickTaskStatus.Pending))
        {
            IReadOnlyList<Domain.Warehouse.BinStock> candidates = await binStocks
                .ListCandidatesAsync(wave.LocationId, task.ItemId, task.ItemVariantId, cancellationToken)
                .ConfigureAwait(false);

            foreach (Domain.Warehouse.BinStock candidate in candidates)
            {
                (Guid, Guid?, Guid?) key = (candidate.BinId, candidate.ItemId, candidate.ItemVariantId);

                if (reservedThisRelease.TryGetValue(key, out Quantity already) && !already.IsZero)
                {
                    candidate.Reserve(already);
                }
            }

            IReadOnlyList<BinAllocation> allocations = allocator.Allocate(task.RequestedQuantity, candidates);

            Quantity totalAllocated = allocations.Aggregate(
                Quantity.Zero(task.RequestedQuantity.UnitOfMeasure), (sum, a) => sum + a.Quantity);

            if (totalAllocated < task.RequestedQuantity)
            {
                throw WarehouseRuleException.InsufficientStockToAllocate(task.RequestedQuantity, totalAllocated);
            }

            foreach (BinAllocation allocation in allocations)
            {
                Domain.Warehouse.BinStock balance = await binStocks
                    .FindAsync(allocation.BinId, task.ItemId, task.ItemVariantId, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new WarehouseNotFoundException("bin stock", allocation.BinId);

                balance.Reserve(allocation.Quantity);

                (Guid, Guid?, Guid?) key = (allocation.BinId, task.ItemId, task.ItemVariantId);

                reservedThisRelease[key] = reservedThisRelease.TryGetValue(key, out Quantity existing)
                    ? existing + allocation.Quantity
                    : allocation.Quantity;
            }

            task.Allocate(allocations[0].BinId, allocations[0].Quantity);

            foreach (BinAllocation extra in allocations.Skip(1))
            {
                PickTask split = PickTask.Create(
                    wave.TenantId, wave.StoreId, wave.Id, task.ItemId, task.ItemVariantId,
                    extra.Quantity, task.OutboundReference);

                split.Allocate(extra.BinId, extra.Quantity);
                waves.AddTask(split);
            }
        }

        wave.Release(clock.UtcNow);

        return Unit.Value;
    }
}
