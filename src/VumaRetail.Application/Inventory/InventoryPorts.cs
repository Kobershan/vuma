using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Inventory;

namespace VumaRetail.Application.Inventory;

/// <summary>Reads and writes <see cref="StockLocation"/> rows.</summary>
/// <remarks>
/// Repositories return tracked entities and never commit. The unit of work is the pipeline's
/// (<c>IUnitOfWork</c>, CLAUDE.md §7 rule 2).
/// </remarks>
public interface IStockLocationRepository
{
    /// <summary>Finds a location by id.</summary>
    Task<StockLocation?> FindAsync(Guid locationId, CancellationToken cancellationToken = default);

    /// <summary>Finds a location by its code, case-insensitively.</summary>
    Task<StockLocation?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Lists every location in the tenant, ordered by code. Not paginated — configuration, not a catalogue.</summary>
    Task<IReadOnlyList<StockLocation>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a new location.</summary>
    void Add(StockLocation location);
}

/// <summary>Reads and writes the append-only <see cref="StockLedgerEntry"/> rows.</summary>
public interface IStockLedgerRepository
{
    /// <summary>
    /// A keyset page of ledger entries for one location, newest first, optionally narrowed to one
    /// stock-keeping unit. See <c>docs/API_STANDARDS.md</c> §8.
    /// </summary>
    Task<(IReadOnlyList<StockLedgerEntry> Entries, bool HasMore)> ListPageAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        Application.Abstractions.KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a ledger entry by id.</summary>
    Task<StockLedgerEntry?> FindAsync(Guid ledgerEntryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every ledger entry correlating to one document — a shipment, a stocktake, a goods receipt. Added
    /// in Stage 14 so <c>IOrderFulfilmentReader</c> can trace an order line's fulfilled quantity back to
    /// the shipment(s) that produced it, to receive a return at the original unit cost (ADR-093).
    /// </summary>
    /// <param name="referenceType">What kind of document to match.</param>
    /// <param name="referenceId">The document's id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<StockLedgerEntry>> ListByReferenceAsync(
        StockReferenceType referenceType, Guid referenceId, CancellationToken cancellationToken = default);

    /// <summary>Appends a new entry. Nothing already added through this method is ever updated or removed.</summary>
    void Add(StockLedgerEntry entry);
}

/// <summary>Reads and writes the <see cref="StockBalance"/> projection.</summary>
public interface IStockBalanceRepository
{
    /// <summary>Finds the balance for one stock-keeping unit at one location, or <c>null</c> if it has never moved there.</summary>
    Task<StockBalance?> FindAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default);

    /// <summary>Every balance held at one location. Not paginated — bounded by the tenant's catalogue.</summary>
    Task<IReadOnlyList<StockBalance>> ListForLocationAsync(Guid locationId, CancellationToken cancellationToken = default);

    /// <summary>Adds a newly opened balance row.</summary>
    void Add(StockBalance balance);
}

/// <summary>Reads and writes <see cref="StockTransfer"/> documents.</summary>
public interface IStockTransferRepository
{
    /// <summary>Finds a transfer by id.</summary>
    Task<StockTransfer?> FindAsync(Guid transferId, CancellationToken cancellationToken = default);

    /// <summary>Records a completed transfer.</summary>
    void Add(StockTransfer transfer);
}

/// <summary>Reads and writes <see cref="StocktakeSession"/> and <see cref="StocktakeLine"/> rows.</summary>
public interface IStocktakeRepository
{
    /// <summary>Finds a session by id.</summary>
    Task<StocktakeSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Every line recorded in a session so far.</summary>
    Task<IReadOnlyList<StocktakeLine>> ListLinesAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Finds an existing line for one stock-keeping unit within a session, for a recount.</summary>
    Task<StocktakeLine?> FindLineAsync(
        Guid sessionId,
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new session.</summary>
    void AddSession(StocktakeSession session);

    /// <summary>Adds a new line.</summary>
    void AddLine(StocktakeLine line);
}

/// <summary>Reads and writes the append-only <see cref="StockReservation"/> rows (Stage 08c).</summary>
/// <remarks>
/// Repositories return tracked entities and never commit. Reservation writes always go through
/// <c>IReservationService</c>, which owns the serialisable transaction and the row lock — a
/// caller adding rows here directly would bypass the availability re-check that is this
/// stage's whole purpose.
/// </remarks>
public interface IStockReservationRepository
{
    /// <summary>Finds the live (<c>Held</c>) row of one reservation chain, or <c>null</c> once it is closed.</summary>
    Task<StockReservation?> FindOpenAsync(Guid reservationId, CancellationToken cancellationToken = default);

    /// <summary>Every row of one reservation chain, oldest first.</summary>
    Task<IReadOnlyList<StockReservation>> ListChainAsync(Guid reservationId, CancellationToken cancellationToken = default);

    /// <summary>Every live (<c>Held</c>) row for one stock-keeping unit at one location.</summary>
    Task<IReadOnlyList<StockReservation>> ListOpenAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The live hold one saga leg took for one stock-keeping unit, or <c>null</c>. A leg holds at
    /// most one row per line: retrying the leg finds this row instead of double-holding (ADR-116),
    /// and the partial unique index underneath makes the find-and-hold race-safe.
    /// </summary>
    Task<StockReservation?> FindLegHoldAsync(
        Guid intentId,
        Guid legId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default);

    /// <summary>Every live hold carrying one group document reference — the compensation set.</summary>
    Task<IReadOnlyList<StockReservation>> ListOpenByGroupRefAsync(
        string groupDocumentRef,
        CancellationToken cancellationToken = default);

    /// <summary>Live holds whose expiry has passed, oldest expiry first, capped at <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<StockReservation>> ListExpiredAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Appends a new row. Nothing already added through this method is ever updated or removed.</summary>
    void Add(StockReservation reservation);
}

/// <summary>Reads and writes the <see cref="AvailableBalance"/> projection (Stage 08c).</summary>
public interface IAvailableBalanceRepository
{
    /// <summary>Finds the position for one stock-keeping unit at one location, or <c>null</c> if nothing was ever held there.</summary>
    Task<AvailableBalance?> FindAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default);

    /// <summary>Every position held at one location. Not paginated — bounded by the tenant's catalogue.</summary>
    Task<IReadOnlyList<AvailableBalance>> ListForLocationAsync(Guid locationId, CancellationToken cancellationToken = default);

    /// <summary>Adds a newly opened position row.</summary>
    void Add(AvailableBalance balance);
}
