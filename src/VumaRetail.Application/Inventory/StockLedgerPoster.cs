using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Inventory;

/// <summary>
/// The one place a command handler goes to post a stock movement: writes the immutable ledger entry,
/// maintains the <see cref="StockBalance"/> projection transactionally alongside it, and raises the
/// event Stage 07 will eventually turn into a journal.
/// </summary>
/// <remarks>
/// Six command handlers (receive, sale issue, adjust, both sides of a transfer, stocktake variance) all
/// need the same three steps in the same order. <c>CONVENTIONS.md</c> §4: "if a handler needs a second
/// handler's logic, extract a domain service; do not call one handler from another" — this is that
/// service, so the weighted-average arithmetic and the ledger/balance/event sequencing exist in exactly
/// one place rather than six.
/// </remarks>
public interface IStockLedgerPoster
{
    /// <summary>Posts a receipt — stock arriving, valued at what it cost.</summary>
    /// <param name="location">Where the stock arrived.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="quantity">How much arrived. Must be positive.</param>
    /// <param name="unitCost">What it cost per unit.</param>
    /// <param name="note">An optional free-text note.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<StockLedgerEntry> ReceiveAsync(
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Money unitCost,
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts a sale issue — stock leaving through a sale, valued at the current weighted-average cost.
    /// Stage 09 (POS) is this movement's real trigger; this is the working command it will call once it
    /// exists (<c>docs/PROGRESS.md</c>'s deferred-integration note).
    /// </summary>
    /// <param name="location">Where the stock left from.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="quantity">How much was sold. Must be positive.</param>
    /// <param name="saleReferenceId">The sale this issue correlates to.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="InventoryRuleException">Insufficient stock is on hand.</exception>
    Task<StockLedgerEntry> IssueForSaleAsync(
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Guid saleReferenceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts a sales return — stock coming back over the counter, valued at what it left at.
    /// </summary>
    /// <param name="location">Where the stock comes back to.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="quantity">How much came back. Must be positive.</param>
    /// <param name="unitCost">
    /// What it cost when it was sold — read off the original sale's ledger entry, not off today's
    /// average. Receiving a return at the current average would let a shop change its own cost of
    /// sales by putting goods through the till and taking them back again.
    /// </param>
    /// <param name="salesReturnReferenceId">The return document this receipt correlates to.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// Added in Stage 10 as the mirror of <see cref="IssueForSaleAsync"/>. It exists rather than the
    /// return calling <see cref="ReceiveAsync"/> because a receipt with no reference is a receipt
    /// nobody can trace back to the document that caused it — the ledger would show stock appearing
    /// with a <see cref="StockReferenceType.Manual"/> label, which is what a stock adjustment looks
    /// like, and the two must not be confusable in a shrinkage investigation.
    /// </remarks>
    Task<StockLedgerEntry> ReceiveForSalesReturnAsync(
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Money unitCost,
        Guid salesReturnReferenceId,
        CancellationToken cancellationToken = default);

    /// <summary>Posts a documented adjustment — a signed correction to on-hand quantity.</summary>
    /// <param name="location">Where the adjustment applies.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="delta">The signed quantity change. Must not be zero.</param>
    /// <param name="reasonCode">Why the adjustment was made.</param>
    /// <param name="unitCost">
    /// The cost to value an increase at, or <c>null</c> to use the current average. Ignored for a
    /// decrease, which always relieves at the current average — an adjustment values stock out at cost,
    /// it does not invent a new one.
    /// </param>
    /// <param name="note">An optional free-text note.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="InventoryRuleException">
    /// A positive adjustment with no existing balance and no supplied cost, or a negative adjustment
    /// beyond what is on hand.
    /// </exception>
    Task<StockLedgerEntry> AdjustAsync(
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity delta,
        AdjustmentReasonCode reasonCode,
        Money? unitCost,
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts an adjustment that came out of a data import, referenced back to the batch that caused it.
    /// </summary>
    /// <param name="location">Where the adjustment applies.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="delta">The signed quantity change. Must not be zero.</param>
    /// <param name="unitCost">
    /// The cost to value an increase at, or <c>null</c> to use the current average. Ignored for a
    /// decrease, exactly as <see cref="AdjustAsync"/> ignores it.
    /// </param>
    /// <param name="importBatchId">The batch this movement came from.</param>
    /// <param name="note">An optional free-text note — the batch number and the source row.</param>
    /// <returns>The posted entry.</returns>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// <para>
    /// Added in Stage 11, and a dedicated method rather than a reference parameter on
    /// <see cref="AdjustAsync"/> for the reason <see cref="ReceiveForSalesReturnAsync"/> gives: a
    /// movement with no reference is a movement nobody can trace back to the document that caused it,
    /// and an untraceable adjustment is indistinguishable from a shrinkage write-off.
    /// </para>
    /// <para>
    /// The reason code is always <see cref="AdjustmentReasonCode.Correction"/>. An import states what
    /// the books should have said; it is not a claim that anything was damaged, lost or found — and
    /// letting a spreadsheet column choose between <c>Loss</c> and <c>Found</c> would put the
    /// shrinkage report at the mercy of whoever typed the file.
    /// </para>
    /// </remarks>
    Task<StockLedgerEntry> AdjustForImportAsync(
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity delta,
        Money? unitCost,
        Guid importBatchId,
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts a transfer as two correlated ledger entries — out of <paramref name="source"/>, in at
    /// <paramref name="destination"/> — both valued at the source's weighted-average cost, so the
    /// transfer moves value rather than creating or destroying it.
    /// </summary>
    /// <param name="source">Where the stock leaves.</param>
    /// <param name="destination">Where the stock arrives. Must differ from <paramref name="source"/>.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="quantity">How much moved. Must be positive.</param>
    /// <param name="transferId">The id correlating both entries — <see cref="StockLedgerEntry.ReferenceId"/> on each.</param>
    /// <param name="note">An optional free-text note.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<(StockLedgerEntry Out, StockLedgerEntry In)> TransferAsync(
        StockLocation source,
        StockLocation destination,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Guid transferId,
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts a stocktake line's variance to the ledger, or does nothing if the line counted exactly what
    /// the system expected.
    /// </summary>
    /// <param name="location">The location counted.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="variance">The signed difference — counted minus system.</param>
    /// <param name="stocktakeSessionId">The session this variance belongs to.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The posted entry, or <c>null</c> if the variance was zero.</returns>
    /// <exception cref="InventoryRuleException">
    /// A positive variance (more counted than expected) with no existing balance — found stock with no
    /// cost basis to value it at; receive it first.
    /// </exception>
    Task<StockLedgerEntry?> PostStocktakeVarianceAsync(
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity variance,
        Guid stocktakeSessionId,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IStockLedgerPoster" />
/// <param name="balances">Stock balance lookup and insertion.</param>
/// <param name="ledger">Ledger entry insertion.</param>
/// <param name="events">Where the financial event a posting raises goes — see its own remarks for the Stage 07 deferral.</param>
/// <param name="clock">
/// The only source of time (<c>CONVENTIONS.md</c> §6). The posted entity's own <c>CreatedAt</c> is not
/// used for the event's <c>OccurredAt</c> — the audit interceptor stamps it later, at
/// <c>SaveChanges</c>, which has not run yet at the point this class publishes.
/// </param>
public sealed class StockLedgerPoster(
    IStockBalanceRepository balances,
    IStockLedgerRepository ledger,
    IInventoryValuationEventPublisher events,
    IClock clock) : IStockLedgerPoster
{
    /// <inheritdoc />
    public Task<StockLedgerEntry> ReceiveAsync(
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Money unitCost,
        string? note,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        return PostReceiptLikeAsync(
            location, itemId, itemVariantId, StockMovementType.Receipt, quantity, unitCost,
            StockReferenceType.Manual, referenceId: null, reasonCode: null, note, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StockLedgerEntry> IssueForSaleAsync(
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Guid saleReferenceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        return PostIssueLikeAsync(
            location, itemId, itemVariantId, StockMovementType.SaleIssue, quantity,
            StockReferenceType.Sale, saleReferenceId, reasonCode: null, note: null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StockLedgerEntry> ReceiveForSalesReturnAsync(
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Money unitCost,
        Guid salesReturnReferenceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        return PostReceiptLikeAsync(
            location, itemId, itemVariantId, StockMovementType.SalesReturn, quantity, unitCost,
            StockReferenceType.SalesReturn, salesReturnReferenceId, reasonCode: null, note: null,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<StockLedgerEntry> AdjustAsync(
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity delta,
        AdjustmentReasonCode reasonCode,
        Money? unitCost,
        string? note,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (delta.IsZero)
        {
            throw InventoryRuleException.QuantityMustBeNonZero();
        }

        if (!delta.IsNegative)
        {
            StockBalance? existing = await balances
                .FindAsync(location.Id, itemId, itemVariantId, cancellationToken)
                .ConfigureAwait(false);

            Money cost = unitCost ?? existing?.AverageCost ?? throw InventoryRuleException.UnitCostRequiredToOpenBalance();

            return await PostReceiptLikeAsync(
                location, itemId, itemVariantId, StockMovementType.Adjustment, delta, cost,
                StockReferenceType.Manual, referenceId: null, reasonCode, note, cancellationToken)
                .ConfigureAwait(false);
        }

        return await PostIssueLikeAsync(
            location, itemId, itemVariantId, StockMovementType.Adjustment, -delta,
            StockReferenceType.Manual, referenceId: null, reasonCode, note, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StockLedgerEntry> AdjustForImportAsync(
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity delta,
        Money? unitCost,
        Guid importBatchId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (delta.IsZero)
        {
            throw InventoryRuleException.QuantityMustBeNonZero();
        }

        if (!delta.IsNegative)
        {
            StockBalance? existing = await balances
                .FindAsync(location.Id, itemId, itemVariantId, cancellationToken)
                .ConfigureAwait(false);

            Money cost = unitCost ?? existing?.AverageCost ?? throw InventoryRuleException.UnitCostRequiredToOpenBalance();

            return await PostReceiptLikeAsync(
                location, itemId, itemVariantId, StockMovementType.Adjustment, delta, cost,
                StockReferenceType.Import, importBatchId, AdjustmentReasonCode.Correction, note,
                cancellationToken)
                .ConfigureAwait(false);
        }

        return await PostIssueLikeAsync(
            location, itemId, itemVariantId, StockMovementType.Adjustment, -delta,
            StockReferenceType.Import, importBatchId, AdjustmentReasonCode.Correction, note,
            cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<(StockLedgerEntry Out, StockLedgerEntry In)> TransferAsync(
        StockLocation source,
        StockLocation destination,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Guid transferId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (source.Id == destination.Id)
        {
            throw InventoryRuleException.TransferLocationsMustDiffer();
        }

        if (quantity.IsNegative || quantity.IsZero)
        {
            throw InventoryRuleException.QuantityMustBePositive();
        }

        StockLedgerEntry outEntry = await PostIssueLikeAsync(
            source, itemId, itemVariantId, StockMovementType.TransferOut, quantity,
            StockReferenceType.Transfer, transferId, reasonCode: null, note, cancellationToken)
            .ConfigureAwait(false);

        // The destination receives at exactly the cost that left the source — a transfer moves value,
        // it does not create or destroy it.
        StockLedgerEntry inEntry = await PostReceiptLikeAsync(
            destination, itemId, itemVariantId, StockMovementType.TransferIn, quantity, outEntry.UnitCost,
            StockReferenceType.Transfer, transferId, reasonCode: null, note, cancellationToken)
            .ConfigureAwait(false);

        return (outEntry, inEntry);
    }

    /// <inheritdoc />
    public async Task<StockLedgerEntry?> PostStocktakeVarianceAsync(
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity variance,
        Guid stocktakeSessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (variance.IsZero)
        {
            return null;
        }

        if (!variance.IsNegative)
        {
            // Found stock has no new cost information — value it at the balance's own current average.
            // With no balance at all there is no average to value it at, and this stage does not
            // fabricate one: receive the item first.
            StockBalance? existing = await balances
                .FindAsync(location.Id, itemId, itemVariantId, cancellationToken)
                .ConfigureAwait(false);

            Money cost = existing?.AverageCost ?? throw InventoryRuleException.UnitCostRequiredToOpenBalance();

            return await PostReceiptLikeAsync(
                location, itemId, itemVariantId, StockMovementType.StocktakeVariance, variance, cost,
                StockReferenceType.Stocktake, stocktakeSessionId, reasonCode: null, note: null, cancellationToken)
                .ConfigureAwait(false);
        }

        return await PostIssueLikeAsync(
            location, itemId, itemVariantId, StockMovementType.StocktakeVariance, -variance,
            StockReferenceType.Stocktake, stocktakeSessionId, reasonCode: null, note: null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Blends a positive quantity into the balance's weighted average (opening a zero balance first if
    /// none exists), posts the ledger entry, and raises the valuation event.
    /// </summary>
    private async Task<StockLedgerEntry> PostReceiptLikeAsync(
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        StockMovementType movementType,
        Quantity quantity,
        Money unitCost,
        StockReferenceType referenceType,
        Guid? referenceId,
        AdjustmentReasonCode? reasonCode,
        string? note,
        CancellationToken cancellationToken)
    {
        StockBalance? balance = await balances
            .FindAsync(location.Id, itemId, itemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (balance is null)
        {
            balance = StockBalance.Open(
                location.TenantId, location.StoreId, location.Id, itemId, itemVariantId,
                quantity.UnitOfMeasure, unitCost.Currency);

            balances.Add(balance);
        }

        balance.ApplyReceipt(quantity, unitCost);

        StockLedgerEntry entry = StockLedgerEntry.Post(
            location.TenantId, location.StoreId, location.Id, itemId, itemVariantId, movementType,
            quantity, unitCost, referenceType, referenceId, reasonCode, note);

        ledger.Add(entry);

        await PublishAsync(entry, cancellationToken).ConfigureAwait(false);

        return entry;
    }

    /// <summary>
    /// Relieves a positive quantity from the balance at its current weighted-average cost, posts the
    /// ledger entry with a negative delta, and raises the valuation event.
    /// </summary>
    private async Task<StockLedgerEntry> PostIssueLikeAsync(
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        StockMovementType movementType,
        Quantity quantity,
        StockReferenceType referenceType,
        Guid? referenceId,
        AdjustmentReasonCode? reasonCode,
        string? note,
        CancellationToken cancellationToken)
    {
        StockBalance? balance = await balances
            .FindAsync(location.Id, itemId, itemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (balance is null)
        {
            throw InventoryRuleException.InsufficientStock(Quantity.Zero(quantity.UnitOfMeasure), quantity);
        }

        Money unitCost = balance.AverageCost;

        balance.ApplyIssue(quantity);

        StockLedgerEntry entry = StockLedgerEntry.Post(
            location.TenantId, location.StoreId, location.Id, itemId, itemVariantId, movementType,
            -quantity, unitCost, referenceType, referenceId, reasonCode, note);

        ledger.Add(entry);

        await PublishAsync(entry, cancellationToken).ConfigureAwait(false);

        return entry;
    }

    private Task PublishAsync(StockLedgerEntry entry, CancellationToken cancellationToken)
        => events.PublishAsync(
            new InventoryValuationEvent(
                entry.TenantId,
                entry.StoreId,
                entry.LocationId,
                entry.Id,
                entry.ItemId,
                entry.ItemVariantId,
                entry.MovementType,
                entry.Quantity,
                entry.Value,
                entry.ReferenceType,
                entry.ReferenceId,
                clock.UtcNow),
            cancellationToken);
}
