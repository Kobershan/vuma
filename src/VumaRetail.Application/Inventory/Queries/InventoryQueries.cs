using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Inventory.Queries;

/// <summary>A stock location, as read back out.</summary>
/// <param name="Id">The location's id.</param>
/// <param name="Code">The location's code.</param>
/// <param name="Name">The location's name.</param>
/// <param name="Type">What kind of location this is.</param>
/// <param name="StoreId">The owning store, or <c>null</c> for a tenant-wide location.</param>
/// <param name="IsActive">Whether the location may still receive or issue stock.</param>
public sealed record StockLocationResult(Guid Id, string Code, string Name, StockLocationType Type, Guid? StoreId, bool IsActive);

/// <summary>Lists every stock location in the tenant. Not paginated — configuration, not a catalogue.</summary>
public sealed record ListStockLocationsQuery : IQuery<IReadOnlyList<StockLocationResult>>;

/// <summary>Lists locations.</summary>
/// <param name="locations">Location lookup.</param>
public sealed class ListStockLocationsQueryHandler(IStockLocationRepository locations)
    : IQueryHandler<ListStockLocationsQuery, IReadOnlyList<StockLocationResult>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<StockLocationResult>> HandleAsync(
        ListStockLocationsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<StockLocation> all = await locations.ListAsync(cancellationToken).ConfigureAwait(false);

        return [.. all.Select(ToResult)];
    }

    internal static StockLocationResult ToResult(StockLocation location)
        => new(location.Id, location.Code, location.Name, location.Type, location.StoreId, location.IsActive);
}

/// <summary>A stock balance, as read back out.</summary>
/// <param name="LocationId">The location.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="QuantityOnHand">What is currently on hand.</param>
/// <param name="AverageCost">The weighted-average cost of one unit currently on hand.</param>
/// <param name="TotalValue">The value of everything on hand.</param>
public sealed record StockBalanceResult(
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity QuantityOnHand,
    Money AverageCost,
    Money TotalValue);

/// <summary>Reads one stock-keeping unit's balance at one location.</summary>
/// <param name="LocationId">The location.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
public sealed record GetStockBalanceQuery(Guid LocationId, Guid? ItemId, Guid? ItemVariantId) : IQuery<StockBalanceResult>;

/// <summary>Reads a balance.</summary>
/// <param name="balances">Balance lookup.</param>
/// <remarks>
/// A location/stock-keeping-unit pair with no balance row is genuinely not found — the row is opened
/// by the first receipt (<see cref="StockBalance.Open"/>), so its absence means nothing has moved yet.
/// Fabricating a zero-quantity result would need a unit of measure and a currency nobody has ever
/// stated for this pairing, which is not this query's business to guess.
/// </remarks>
public sealed class GetStockBalanceQueryHandler(IStockBalanceRepository balances)
    : IQueryHandler<GetStockBalanceQuery, StockBalanceResult>
{
    /// <inheritdoc />
    public async Task<StockBalanceResult> HandleAsync(GetStockBalanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        StockBalance balance = await balances
            .FindAsync(query.LocationId, query.ItemId, query.ItemVariantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock balance", query.ItemId ?? query.ItemVariantId ?? Guid.Empty);

        return ToResult(balance);
    }

    internal static StockBalanceResult ToResult(StockBalance balance) => new(
        balance.LocationId,
        balance.ItemId,
        balance.ItemVariantId,
        balance.QuantityOnHand,
        balance.AverageCost,
        balance.TotalValue);
}

/// <summary>Lists every balance held at one location. Not paginated — bounded by the tenant's catalogue.</summary>
/// <param name="LocationId">The location.</param>
public sealed record ListStockBalancesQuery(Guid LocationId) : IQuery<IReadOnlyList<StockBalanceResult>>;

/// <summary>Lists a location's balances.</summary>
/// <param name="balances">Balance lookup.</param>
public sealed class ListStockBalancesQueryHandler(IStockBalanceRepository balances)
    : IQueryHandler<ListStockBalancesQuery, IReadOnlyList<StockBalanceResult>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<StockBalanceResult>> HandleAsync(
        ListStockBalancesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<StockBalance> all = await balances.ListForLocationAsync(query.LocationId, cancellationToken).ConfigureAwait(false);

        return [.. all.Select(GetStockBalanceQueryHandler.ToResult)];
    }
}

/// <summary>One posted, immutable stock movement, as read back out.</summary>
/// <param name="Id">The entry's id.</param>
/// <param name="LocationId">The location affected.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="MovementType">What kind of movement this is.</param>
/// <param name="Quantity">The signed quantity delta.</param>
/// <param name="UnitCost">The value of one unit at the moment of this movement.</param>
/// <param name="Value">The total value this movement carries.</param>
/// <param name="ReferenceType">What kind of document this correlates to, if any.</param>
/// <param name="ReferenceId">The correlating document's id.</param>
/// <param name="ReasonCode">Why an adjustment was made, if this is one.</param>
/// <param name="Note">An optional free-text note.</param>
/// <param name="CreatedAt">When this entry was posted, UTC.</param>
public sealed record StockLedgerEntryResult(
    Guid Id,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    StockMovementType MovementType,
    Quantity Quantity,
    Money UnitCost,
    Money Value,
    StockReferenceType ReferenceType,
    Guid? ReferenceId,
    AdjustmentReasonCode? ReasonCode,
    string? Note,
    DateTimeOffset CreatedAt);

/// <summary>
/// A keyset-paginated page of one location's ledger entries, newest first
/// (<c>docs/API_STANDARDS.md</c> §8), optionally narrowed to one stock-keeping unit.
/// </summary>
/// <param name="LocationId">The location.</param>
/// <param name="ItemId">Narrows to one item, when it has no variants.</param>
/// <param name="ItemVariantId">Narrows to one variant.</param>
/// <param name="Limit">How many rows to return. Clamped to <see cref="Paging.MaxLimit"/>.</param>
/// <param name="After">The opaque cursor of the last row the caller already has, or <c>null</c> for the first page.</param>
public sealed record ListStockLedgerEntriesQuery(
    Guid LocationId,
    Guid? ItemId = null,
    Guid? ItemVariantId = null,
    int? Limit = null,
    string? After = null) : IQuery<PageResult<StockLedgerEntryResult>>;

/// <summary>Lists a location's ledger entries a page at a time.</summary>
/// <param name="ledger">Ledger entry lookup.</param>
public sealed class ListStockLedgerEntriesQueryHandler(IStockLedgerRepository ledger)
    : IQueryHandler<ListStockLedgerEntriesQuery, PageResult<StockLedgerEntryResult>>
{
    /// <inheritdoc />
    public async Task<PageResult<StockLedgerEntryResult>> HandleAsync(
        ListStockLedgerEntriesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);
        KeysetCursor? after = KeysetCursor.TryDecode(query.After);

        (IReadOnlyList<StockLedgerEntry> page, bool hasMore) = await ledger
            .ListPageAsync(query.LocationId, query.ItemId, query.ItemVariantId, after, limit, cancellationToken)
            .ConfigureAwait(false);

        // Sorted newest-first by (CreatedAt, Id) in the repository; the cursor carries the same pair so
        // a page boundary can never repeat or skip a row under concurrent inserts.
        string? nextCursor = hasMore && page.Count > 0
            ? new KeysetCursor(page[^1].CreatedAt.ToString("O"), page[^1].Id).Encode()
            : null;

        return new PageResult<StockLedgerEntryResult>([.. page.Select(ToResult)], nextCursor, hasMore);
    }

    private static StockLedgerEntryResult ToResult(StockLedgerEntry entry) => new(
        entry.Id,
        entry.LocationId,
        entry.ItemId,
        entry.ItemVariantId,
        entry.MovementType,
        entry.Quantity,
        entry.UnitCost,
        entry.Value,
        entry.ReferenceType,
        entry.ReferenceId,
        entry.ReasonCode,
        entry.Note,
        entry.CreatedAt);
}

/// <summary>A completed transfer, as read back out.</summary>
/// <param name="Id">The transfer's id.</param>
/// <param name="SourceLocationId">Where stock left.</param>
/// <param name="DestinationLocationId">Where stock arrived.</param>
/// <param name="ItemId">The item transferred, when it has no variants.</param>
/// <param name="ItemVariantId">The variant transferred.</param>
/// <param name="Quantity">How much moved.</param>
/// <param name="UnitCost">The source's average cost per unit, carried to the destination unchanged.</param>
/// <param name="Value">The value that moved.</param>
/// <param name="OutEntryId">The ledger entry posted at the source.</param>
/// <param name="InEntryId">The ledger entry posted at the destination.</param>
/// <param name="Note">An optional free-text note.</param>
public sealed record StockTransferResult(
    Guid Id,
    Guid SourceLocationId,
    Guid DestinationLocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Quantity,
    Money UnitCost,
    Money Value,
    Guid OutEntryId,
    Guid InEntryId,
    string? Note);

/// <summary>Reads one transfer by id.</summary>
/// <param name="TransferId">The transfer.</param>
public sealed record GetStockTransferQuery(Guid TransferId) : IQuery<StockTransferResult>;

/// <summary>Reads a transfer.</summary>
/// <param name="transfers">Transfer lookup.</param>
public sealed class GetStockTransferQueryHandler(IStockTransferRepository transfers)
    : IQueryHandler<GetStockTransferQuery, StockTransferResult>
{
    /// <inheritdoc />
    public async Task<StockTransferResult> HandleAsync(GetStockTransferQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        StockTransfer transfer = await transfers.FindAsync(query.TransferId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock transfer", query.TransferId);

        return new StockTransferResult(
            transfer.Id,
            transfer.SourceLocationId,
            transfer.DestinationLocationId,
            transfer.ItemId,
            transfer.ItemVariantId,
            transfer.Quantity,
            transfer.UnitCost,
            transfer.Value,
            transfer.OutEntryId,
            transfer.InEntryId,
            transfer.Note);
    }
}

/// <summary>A recorded stocktake line, as read back out.</summary>
/// <param name="Id">The line's id.</param>
/// <param name="ItemId">The item counted, when it has no variants.</param>
/// <param name="ItemVariantId">The variant counted.</param>
/// <param name="SystemQuantity">What the system reported at the moment this line was recorded.</param>
/// <param name="CountedQuantity">What was physically found.</param>
/// <param name="Variance">The signed difference — counted minus system.</param>
public sealed record StocktakeLineResult(
    Guid Id,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity SystemQuantity,
    Quantity CountedQuantity,
    Quantity Variance);

/// <summary>A stocktake session and its lines so far, as read back out.</summary>
/// <param name="Id">The session's id.</param>
/// <param name="LocationId">The location being counted.</param>
/// <param name="Status">Where the session stands.</param>
/// <param name="FinalizedAt">When the session was finalized, or <c>null</c> while still open.</param>
/// <param name="Lines">Every line recorded so far.</param>
public sealed record StocktakeSessionResult(
    Guid Id,
    Guid LocationId,
    StocktakeStatus Status,
    DateTimeOffset? FinalizedAt,
    IReadOnlyList<StocktakeLineResult> Lines);

/// <summary>Reads a stocktake session and its lines.</summary>
/// <param name="SessionId">The session.</param>
public sealed record GetStocktakeSessionQuery(Guid SessionId) : IQuery<StocktakeSessionResult>;

/// <summary>Reads a session.</summary>
/// <param name="stocktakes">Session and line lookup.</param>
public sealed class GetStocktakeSessionQueryHandler(IStocktakeRepository stocktakes)
    : IQueryHandler<GetStocktakeSessionQuery, StocktakeSessionResult>
{
    /// <inheritdoc />
    public async Task<StocktakeSessionResult> HandleAsync(
        GetStocktakeSessionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        StocktakeSession session = await stocktakes.FindSessionAsync(query.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stocktake session", query.SessionId);

        IReadOnlyList<StocktakeLine> lines = await stocktakes.ListLinesAsync(query.SessionId, cancellationToken).ConfigureAwait(false);

        return new StocktakeSessionResult(
            session.Id,
            session.LocationId,
            session.Status,
            session.FinalizedAt,
            [.. lines.Select(line => new StocktakeLineResult(
                line.Id, line.ItemId, line.ItemVariantId, line.SystemQuantity, line.CountedQuantity, line.Variance))]);
    }
}
