using VumaRetail.Domain.Pos;

namespace VumaRetail.Application.Pos;

/// <summary>Reads and writes <see cref="Sale"/> aggregates.</summary>
/// <remarks>
/// <para>
/// Repositories return tracked entities and never commit. The unit of work is the pipeline's
/// (<c>IUnitOfWork</c>, CLAUDE.md §7 rule 2).
/// </para>
/// <para>
/// There is no <c>AddLine</c> or <c>AddTender</c> here on purpose. A line and a tender are only ever
/// reached through the sale that owns them, so they are added to the tracked aggregate and saved with
/// it — a repository method that could insert a line against a sale the caller never loaded is a way
/// to write a line onto a completed sale without the aggregate refusing it.
/// </para>
/// </remarks>
public interface ISaleRepository
{
    /// <summary>
    /// Finds a sale with its lines and tenders loaded, or <c>null</c>. Loading the children is not
    /// optional: the aggregate recomputes its totals from them, so a sale loaded without them would
    /// zero itself on the next write.
    /// </summary>
    /// <param name="saleId">The sale.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Sale?> FindAsync(Guid saleId, CancellationToken cancellationToken = default);

    /// <summary>Finds a sale by its receipt number, with children loaded.</summary>
    /// <param name="saleNumber">The receipt number.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Sale?> FindByNumberAsync(string saleNumber, CancellationToken cancellationToken = default);

    /// <summary>Every sale set aside at one terminal, oldest first. Bounded by what a till can hold.</summary>
    /// <param name="terminalId">The terminal.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Sale>> ListParkedAsync(Guid terminalId, CancellationToken cancellationToken = default);

    /// <summary>Every sale rung up on one till session, with children loaded.</summary>
    /// <param name="tillSessionId">The session.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Sale>> ListForSessionAsync(Guid tillSessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many sales on a session are still <see cref="SaleStatus.Open"/> or
    /// <see cref="SaleStatus.Parked"/> — the check that stops a drawer being counted against a moving
    /// total.
    /// </summary>
    /// <param name="tillSessionId">The session.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<int> CountUnfinishedForSessionAsync(Guid tillSessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sale lines whose stock issue was refused and whose sale completed anyway — the reconciliation
    /// queue ADR-073 creates. Newest first.
    /// </summary>
    /// <param name="locationId">Narrow to one stock location, or <c>null</c> for the whole tenant.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<(Sale Sale, SaleLine Line)>> ListRefusedStockIssuesAsync(
        Guid? locationId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Adds a new sale.</summary>
    /// <param name="sale">The sale.</param>
    void Add(Sale sale);
}

/// <summary>Reads and writes <see cref="TillSession"/> rows.</summary>
public interface ITillSessionRepository
{
    /// <summary>Finds a session by id.</summary>
    /// <param name="tillSessionId">The session.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<TillSession?> FindAsync(Guid tillSessionId, CancellationToken cancellationToken = default);

    /// <summary>The open session at a terminal, or <c>null</c> when the till is not trading.</summary>
    /// <param name="terminalId">The terminal.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<TillSession?> FindOpenForTerminalAsync(Guid terminalId, CancellationToken cancellationToken = default);

    /// <summary>Recent sessions, newest first, optionally narrowed to one terminal.</summary>
    /// <param name="terminalId">The terminal, or <c>null</c> for every terminal in the tenant.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<TillSession>> ListRecentAsync(
        Guid? terminalId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Adds a newly opened session.</summary>
    /// <param name="session">The session.</param>
    void Add(TillSession session);
}

/// <summary>Reads and writes the append-only <see cref="ReceiptPrint"/> log.</summary>
public interface IReceiptPrintRepository
{
    /// <summary>How many times a sale's receipt has already been printed.</summary>
    /// <param name="saleId">The sale.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<int> CountForSaleAsync(Guid saleId, CancellationToken cancellationToken = default);

    /// <summary>Every print of one sale's receipt, oldest first.</summary>
    /// <param name="saleId">The sale.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<ReceiptPrint>> ListForSaleAsync(Guid saleId, CancellationToken cancellationToken = default);

    /// <summary>Appends a print. Nothing added through this method is ever updated or removed.</summary>
    /// <param name="print">The print.</param>
    void Add(ReceiptPrint print);
}
