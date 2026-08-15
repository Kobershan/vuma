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
