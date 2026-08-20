using VumaRetail.Application.Inventory;
using VumaRetail.Application.Warehouse;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Application.Orders;

/// <summary>
/// What Stage 13's <c>PickTask</c>/<c>PickWave</c> state says about one order line right now: how much is
/// allocated, how much has shipped, and whether a short pick is in the mix.
/// </summary>
/// <param name="AllocatedQuantity">
/// The sum of <c>PickTask.AllocatedQuantity</c> across every open (not shipped, not cancelled) task
/// carrying this line's id as <c>OutboundReference</c> — business rule 1's own definition of "allocated".
/// </param>
/// <param name="FulfilledQuantity">
/// The sum of <c>PickTask.PickedQuantity</c> across every task whose parent wave has shipped (business
/// rule 4).
/// </param>
/// <param name="HasShortPick">
/// True when at least one shipped task under-picked its allocation. Business rule 4's distinction: a
/// short pick is Stage 13's discovered stock discrepancy, never this stage's <c>Backordered</c>.
/// </param>
/// <param name="OpenPickTaskIds">
/// The ids of every open task found — what <c>CancelOrderLineCommand</c> hands to Stage 13's own
/// <c>CancelPickTaskCommand</c> logic (business rule 7).
/// </param>
public sealed record OrderLineFulfilmentSnapshot(
    Quantity AllocatedQuantity,
    Quantity FulfilledQuantity,
    bool HasShortPick,
    IReadOnlyList<Guid> OpenPickTaskIds);

/// <summary>
/// The one seam that reads Stage 13 <c>PickTask</c>/<c>PickWave</c> state back for Stage 14. Implemented
/// against Stage 13's existing Application-layer ports — an Application-layer cross-module read, the same
/// shape Stage 10 already uses against Stage 08's <c>IStockLocationRepository</c> — never against
/// Infrastructure, and never the other way round: Stage 13 references nothing here
/// (<c>OrdersRulesTests</c> asserts it).
/// </summary>
public interface IOrderFulfilmentReader
{
    /// <summary>
    /// What a location can still promise for one stock-keeping unit right now: on-hand less what open
    /// tasks (any order, any wave) have already reserved from it.
    /// </summary>
    /// <param name="locationId">The location.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="unitOfMeasure">The unit of measure to express the result in.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Never negative — clamped to zero rather than reporting a promise already broken.</returns>
    Task<Quantity> GetAvailableToPromiseAsync(
        Guid locationId, Guid? itemId, Guid? itemVariantId, string unitOfMeasure, CancellationToken cancellationToken = default);

    /// <summary>Everything Stage 13 currently knows about one order line's fulfilment.</summary>
    /// <param name="orderLineId">The order line, as it appears in <c>PickTask.OutboundReference</c>.</param>
    /// <param name="unitOfMeasure">The unit of measure to express the result in.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<OrderLineFulfilmentSnapshot> GetLineFulfilmentAsync(
        Guid orderLineId, string unitOfMeasure, CancellationToken cancellationToken = default);

    /// <summary>
    /// The quantity-weighted average unit cost across every shipment that has fulfilled this order line —
    /// what a <c>SalesOrderReturn</c> receives stock back at (ADR-093).
    /// </summary>
    /// <param name="orderLineId">The order line.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="currency">The currency to express the result in.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>null</c> when no shipment can be traced for this line — nothing has shipped, or the shipment's ledger entry cannot be found.</returns>
    Task<Money?> GetShipmentUnitCostAsync(
        Guid orderLineId, Guid? itemId, Guid? itemVariantId, string currency, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IOrderFulfilmentReader" />
/// <param name="waves">Stage 13's pick wave/task port.</param>
/// <param name="shipments">Stage 13's shipment confirmation port.</param>
/// <param name="ledger">Stage 08's ledger port — where a shipment's unit cost was actually recorded.</param>
/// <param name="balances">Stage 08's balance port — a location's current on-hand.</param>
public sealed class OrderFulfilmentReader(
    IPickWaveRepository waves,
    IShipmentConfirmationRepository shipments,
    IStockLedgerRepository ledger,
    IStockBalanceRepository balances) : IOrderFulfilmentReader
{
    /// <inheritdoc />
    public async Task<Quantity> GetAvailableToPromiseAsync(
        Guid locationId, Guid? itemId, Guid? itemVariantId, string unitOfMeasure, CancellationToken cancellationToken = default)
    {
        StockBalance? balance = await balances
            .FindAsync(locationId, itemId, itemVariantId, cancellationToken)
            .ConfigureAwait(false);

        Quantity onHand = balance?.QuantityOnHand ?? Quantity.Zero(unitOfMeasure);

        Quantity openAllocated = await waves
            .SumOpenAllocatedQuantityAsync(locationId, itemId, itemVariantId, unitOfMeasure, cancellationToken)
            .ConfigureAwait(false);

        Quantity availableToPromise = onHand - openAllocated;

        return availableToPromise.IsNegative ? Quantity.Zero(unitOfMeasure) : availableToPromise;
    }

    /// <inheritdoc />
    public async Task<OrderLineFulfilmentSnapshot> GetLineFulfilmentAsync(
        Guid orderLineId, string unitOfMeasure, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PickTask> tasks = await waves
            .ListTasksByOutboundReferenceAsync(orderLineId.ToString(), cancellationToken)
            .ConfigureAwait(false);

        if (tasks.Count == 0)
        {
            return new OrderLineFulfilmentSnapshot(Quantity.Zero(unitOfMeasure), Quantity.Zero(unitOfMeasure), false, []);
        }

        Dictionary<Guid, PickWave?> waveCache = [];
        decimal allocated = 0m;
        decimal fulfilled = 0m;
        bool hasShortPick = false;
        List<Guid> openTaskIds = [];

        foreach (PickTask task in tasks)
        {
            PickWave? wave = await FindWaveAsync(waveCache, task.PickWaveId, cancellationToken).ConfigureAwait(false);

            if (wave?.Status == PickWaveStatus.Shipped)
            {
                fulfilled += task.PickedQuantityValue ?? 0m;

                if (task.Status == PickTaskStatus.ShortPicked)
                {
                    hasShortPick = true;
                }

                continue;
            }

            bool waveStillOpen = wave is not null && wave.Status != PickWaveStatus.Cancelled;

            if (waveStillOpen && task.Status != PickTaskStatus.Cancelled)
            {
                allocated += task.AllocatedQuantityValue ?? 0m;
                openTaskIds.Add(task.Id);
            }
        }

        return new OrderLineFulfilmentSnapshot(
            new Quantity(allocated, unitOfMeasure), new Quantity(fulfilled, unitOfMeasure), hasShortPick, openTaskIds);
    }

    /// <inheritdoc />
    public async Task<Money?> GetShipmentUnitCostAsync(
        Guid orderLineId, Guid? itemId, Guid? itemVariantId, string currency, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PickTask> tasks = await waves
            .ListTasksByOutboundReferenceAsync(orderLineId.ToString(), cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, PickWave?> waveCache = [];
        decimal totalQuantity = 0m;
        decimal totalValue = 0m;

        foreach (IGrouping<Guid, PickTask> group in tasks.GroupBy(task => task.PickWaveId))
        {
            PickWave? wave = await FindWaveAsync(waveCache, group.Key, cancellationToken).ConfigureAwait(false);

            if (wave is not { Status: PickWaveStatus.Shipped })
            {
                continue;
            }

            ShipmentConfirmation? shipment = await shipments
                .FindForWaveAsync(wave.Id, cancellationToken)
                .ConfigureAwait(false);

            if (shipment is null)
            {
                continue;
            }

            IReadOnlyList<StockLedgerEntry> entries = await ledger
                .ListByReferenceAsync(StockReferenceType.Shipment, shipment.Id, cancellationToken)
                .ConfigureAwait(false);

            StockLedgerEntry? match = entries
                .FirstOrDefault(entry => entry.ItemId == itemId && entry.ItemVariantId == itemVariantId);

            if (match is null)
            {
                continue;
            }

            decimal pickedQuantity = group.Sum(task => task.PickedQuantityValue ?? 0m);
            totalQuantity += pickedQuantity;
            totalValue += pickedQuantity * match.UnitCost.Amount;
        }

        return totalQuantity <= 0m ? null : new Money(totalValue / totalQuantity, currency);
    }

    private async Task<PickWave?> FindWaveAsync(
        Dictionary<Guid, PickWave?> cache, Guid pickWaveId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(pickWaveId, out PickWave? cached))
        {
            return cached;
        }

        PickWave? wave = await waves.FindAsync(pickWaveId, cancellationToken).ConfigureAwait(false);
        cache[pickWaveId] = wave;
        return wave;
    }
}
