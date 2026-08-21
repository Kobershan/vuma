using VumaRetail.Application.Warehouse;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Application.Orders;

/// <summary>
/// The one place <c>ConfirmOrderCommand</c> and <c>ReattemptBackorderedAllocationsCommand</c> share:
/// attempt to allocate some quantity of a line's demand at a location, through Stage 13's own
/// <c>PickTask</c>/bin-allocation shape (business rules 1 and 2).
/// </summary>
/// <remarks>
/// <para>
/// Not a new allocation engine (ADR-092, the stage document's "what this stage does not own"). This
/// does, inline and in the same transaction, exactly what <c>AddPickTaskCommand</c> followed by
/// <c>ReleasePickWaveCommand</c> already does for a caller-supplied wave — construct a
/// <see cref="PickTask"/> against an open <see cref="PickWave"/> and allocate it against
/// <see cref="IPickAllocationStrategy"/>'s candidate bins. It is written here rather than invoked
/// through <c>IDispatcher</c> because a command handler does not call another command handler
/// (<c>CLAUDE.md</c> §7 rule 2's transaction boundary is the pipeline's, not nested) — the same reason
/// <c>SalesReturnCompletionService</c> is an extracted service rather than one handler calling another.
/// </para>
/// <para>
/// <b>What actually fits is decided by the bin allocator, not by <c>IOrderFulfilmentReader</c>'s
/// answer alone.</b> Available-to-promise is computed from a location's on-hand less open
/// reservations, which can outrun what is actually shelved into a bin (stock received but not yet put
/// away). Rather than let that mismatch throw and fail the whole order — the one thing business rule 2
/// says never happens — whatever the allocator can actually place into a bin is what counts as
/// allocated, and the difference from what was attempted is added back to the caller's backorder.
/// </para>
/// </remarks>
internal static class OrderAllocation
{
    /// <summary>
    /// Attempts to allocate <paramref name="attemptQuantity"/> of <paramref name="line"/>'s demand,
    /// opening a wave under <paramref name="wave"/> only if one is not already open for this batch and
    /// something is actually being attempted.
    /// </summary>
    /// <param name="waves">Wave and task insertion.</param>
    /// <param name="binStocks">The allocator's candidate pool.</param>
    /// <param name="allocator">Decides which bin(s) satisfy the demand.</param>
    /// <param name="line">The order line the demand is raised for. Never written here — the caller
    /// records the outcome via <see cref="SalesOrderLine.RecordAllocationOutcome"/>.</param>
    /// <param name="location">Where the demand is raised against.</param>
    /// <param name="attemptQuantity">How much to try to allocate. A no-op when zero.</param>
    /// <param name="wave">A wave already opened earlier in this batch, or <c>null</c> to open one on demand.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The wave used (new or the one passed in, or <c>null</c> if nothing was attempted), and how much was actually allocated.</returns>
    internal static async Task<(PickWave? Wave, Quantity Allocated)> AttemptAsync(
        IPickWaveRepository waves,
        IBinStockRepository binStocks,
        IPickAllocationStrategy allocator,
        SalesOrderLine line,
        StockLocation location,
        Quantity attemptQuantity,
        PickWave? wave,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(location);

        if (attemptQuantity.IsZero || attemptQuantity.IsNegative)
        {
            return (wave, Quantity.Zero(attemptQuantity.UnitOfMeasure));
        }

        wave ??= OpenWave(waves, location);

        PickTask task = PickTask.Create(
            location.TenantId, location.StoreId, wave.Id, line.ItemId, line.ItemVariantId,
            attemptQuantity, line.Id.ToString());

        waves.AddTask(task);

        IReadOnlyList<BinStock> candidates = await binStocks
            .ListCandidatesAsync(location.Id, line.ItemId, line.ItemVariantId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<BinAllocation> allocations = allocator.Allocate(attemptQuantity, candidates);

        if (allocations.Count == 0)
        {
            task.Cancel();
            return (wave, Quantity.Zero(attemptQuantity.UnitOfMeasure));
        }

        task.Allocate(allocations[0].BinId, allocations[0].Quantity);
        Quantity total = allocations[0].Quantity;

        foreach (BinAllocation extra in allocations.Skip(1))
        {
            PickTask split = PickTask.Create(
                location.TenantId, location.StoreId, wave.Id, line.ItemId, line.ItemVariantId,
                extra.Quantity, line.Id.ToString());

            split.Allocate(extra.BinId, extra.Quantity);
            waves.AddTask(split);
            total += extra.Quantity;
        }

        return (wave, total);
    }

    private static PickWave OpenWave(IPickWaveRepository waves, StockLocation location)
    {
        PickWave wave = PickWave.Open(location.TenantId, location.StoreId, location.Id);
        waves.AddWave(wave);
        return wave;
    }
}
