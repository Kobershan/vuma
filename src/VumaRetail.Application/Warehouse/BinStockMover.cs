using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Application.Warehouse;

/// <summary>
/// The one place a handler goes to move quantity between bins: posts the immutable
/// <see cref="BinStockMovement"/> row(s) and maintains the <see cref="BinStock"/> projection
/// transactionally alongside them. The bin-level mirror of Stage 08's <c>IStockLedgerPoster</c>.
/// </summary>
/// <remarks>
/// Quantity only — no valuation, no financial event. Every method here changes only where inside a
/// location stock sits, never what the location holds; a movement that changes the location's own
/// on-hand quantity goes through <c>IStockLedgerPoster</c> instead (business rules 2–4).
/// </remarks>
public interface IBinStockMover
{
    /// <summary>Shelves stock into a bin from unbinned (dock) storage — the putaway movement.</summary>
    /// <param name="bin">The destination bin.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="quantity">How much was shelved. Must be positive.</param>
    /// <param name="putawayTaskId">The <see cref="PutawayTask"/> this movement belongs to.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<BinStockMovement> PutawayAsync(
        Bin bin,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Guid putawayTaskId,
        CancellationToken cancellationToken = default);

    /// <summary>Relieves a bin for a confirmed pick.</summary>
    /// <param name="bin">The bin picked from.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="quantity">How much was picked. Must be positive.</param>
    /// <param name="pickTaskId">The <see cref="PickTask"/> this movement belongs to.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="WarehouseRuleException">The bin does not hold enough of this stock-keeping unit.</exception>
    Task<BinStockMovement> PickAsync(
        Bin bin,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Guid pickTaskId,
        CancellationToken cancellationToken = default);

    /// <summary>Moves stock from one bin to another within the same location.</summary>
    /// <param name="source">Where the stock leaves.</param>
    /// <param name="destination">Where the stock arrives.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="quantity">How much moved. Must be positive.</param>
    /// <param name="transferId">The id correlating both movements.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<(BinStockMovement Out, BinStockMovement In)> InternalTransferAsync(
        Bin source,
        Bin destination,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Guid transferId,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IBinStockMover" />
/// <param name="binStocks">Bin balance lookup and insertion.</param>
/// <param name="movements">Movement insertion.</param>
public sealed class BinStockMover(IBinStockRepository binStocks, IBinStockMovementRepository movements) : IBinStockMover
{
    /// <inheritdoc />
    public async Task<BinStockMovement> PutawayAsync(
        Bin bin,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Guid putawayTaskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bin);

        BinStock balance = await OpenOrFindAsync(bin, itemId, itemVariantId, quantity.UnitOfMeasure, cancellationToken).ConfigureAwait(false);
        balance.ApplyIn(quantity);

        BinStockMovement movement = BinStockMovement.Post(
            bin.TenantId, bin.StoreId, bin.Id, itemId, itemVariantId, BinStockMovementType.PutawayIn,
            quantity, BinStockReferenceType.Putaway, putawayTaskId);

        movements.Add(movement);

        return movement;
    }

    /// <inheritdoc />
    public async Task<BinStockMovement> PickAsync(
        Bin bin,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Guid pickTaskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bin);

        BinStock? balance = await binStocks.FindAsync(bin.Id, itemId, itemVariantId, cancellationToken).ConfigureAwait(false);

        if (balance is null)
        {
            throw WarehouseRuleException.InsufficientBinStock(Quantity.Zero(quantity.UnitOfMeasure), quantity);
        }

        balance.ApplyOut(quantity);

        BinStockMovement movement = BinStockMovement.Post(
            bin.TenantId, bin.StoreId, bin.Id, itemId, itemVariantId, BinStockMovementType.PickReserve,
            -quantity, BinStockReferenceType.Pick, pickTaskId);

        movements.Add(movement);

        return movement;
    }

    /// <inheritdoc />
    public async Task<(BinStockMovement Out, BinStockMovement In)> InternalTransferAsync(
        Bin source,
        Bin destination,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Guid transferId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        BinStock? sourceBalance = await binStocks.FindAsync(source.Id, itemId, itemVariantId, cancellationToken).ConfigureAwait(false);

        if (sourceBalance is null)
        {
            throw WarehouseRuleException.InsufficientBinStock(Quantity.Zero(quantity.UnitOfMeasure), quantity);
        }

        sourceBalance.ApplyOut(quantity);

        BinStockMovement outMovement = BinStockMovement.Post(
            source.TenantId, source.StoreId, source.Id, itemId, itemVariantId, BinStockMovementType.InternalTransferOut,
            -quantity, BinStockReferenceType.InternalTransfer, transferId);

        movements.Add(outMovement);

        BinStock destinationBalance = await OpenOrFindAsync(destination, itemId, itemVariantId, quantity.UnitOfMeasure, cancellationToken).ConfigureAwait(false);
        destinationBalance.ApplyIn(quantity);

        BinStockMovement inMovement = BinStockMovement.Post(
            destination.TenantId, destination.StoreId, destination.Id, itemId, itemVariantId, BinStockMovementType.InternalTransferIn,
            quantity, BinStockReferenceType.InternalTransfer, transferId);

        movements.Add(inMovement);

        return (outMovement, inMovement);
    }

    private async Task<BinStock> OpenOrFindAsync(
        Bin bin, Guid? itemId, Guid? itemVariantId, string unitOfMeasure, CancellationToken cancellationToken)
    {
        BinStock? balance = await binStocks.FindAsync(bin.Id, itemId, itemVariantId, cancellationToken).ConfigureAwait(false);

        if (balance is not null)
        {
            return balance;
        }

        balance = BinStock.Open(bin.TenantId, bin.StoreId, bin.Id, itemId, itemVariantId, unitOfMeasure);
        binStocks.Add(balance);

        return balance;
    }
}
