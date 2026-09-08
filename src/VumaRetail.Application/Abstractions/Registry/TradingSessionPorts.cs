#pragma warning disable CS1591
using VumaRetail.Domain.Registry.Trading;

namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>Reads and writes mixed-basket trading sessions (Stage 09b).</summary>
/// <remarks>
/// Tracked entities, never <c>Update</c>. The pipeline's unit of work covers the company
/// database only, so this repository commits the registry boundary itself — the same standing
/// as <c>CompanyLinkService</c>, which commits its own context explicitly.
/// </remarks>
public interface ITradingSessionRepository
{
    Task<TradingSession?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TradingSession?> FindByNumberAsync(string sessionNumber, CancellationToken cancellationToken = default);
    Task<TradingSession?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TradingSession>> ListOpenAsync(CancellationToken cancellationToken = default);
    void Add(TradingSession session);

    /// <summary>Commits the registry boundary. Called by command handlers after mutating the session.</summary>
    Task<int> CommitSessionAsync(CancellationToken cancellationToken = default);
}

/// <summary>Completes a tendered trading session: one leg per segment, each in its own company's database.</summary>
/// <remarks>
/// An application service, NOT a command handler: a handler may resolve at most one company
/// context, while a completion spans several (ADR-116).
/// </remarks>
public interface IMixedBasketCompletionService
{
    /// <summary>Completes the session, returning one posted invoice per company.</summary>
    Task<IReadOnlyList<CompletedSegment>> CompleteAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

/// <summary>One posted company segment: the sale, the invoice, and the receipt behind it.</summary>
/// <param name="CompanyId">The company whose books carry it.</param>
/// <param name="SaleId">The completed till sale.</param>
/// <param name="InvoiceId">The posted tax invoice.</param>
/// <param name="InvoiceNumber">The invoice's number in that company's <c>INV</c> series.</param>
public sealed record CompletedSegment(Guid CompanyId, Guid SaleId, Guid InvoiceId, string InvoiceNumber);

/// <summary>Opens child scopes bound to one company for reservation calls (Stage 09b).</summary>
/// <remarks>
/// Handlers never touch company databases. Reservation legs run here, one scope per call;
/// money legs open their one company context in <c>MixedBasketCompletionService</c> (the
/// file's single <c>CreateAsync</c> site, arch-test counted like <c>InvoiceIssuingService</c>).
/// </remarks>
public interface ITradingCompanyGateway
{
    /// <summary>Runs a reservation call (reserve/consume/release) in the company's scope.</summary>
    /// <remarks>
    /// No explicit context is opened here: <c>IReservationService</c> opens its own through
    /// the company factory, bound by the scope's company — one context per scope either way.
    /// </remarks>
    Task<T> RunReservationAsync<T>(
        Guid tenantId,
        Guid companyId,
        Func<IServiceProvider, Task<T>> action,
        CancellationToken cancellationToken = default);

    /// <summary>Runs a reservation call that returns nothing (release, consume).</summary>
    Task RunReservationAsync(
        Guid tenantId,
        Guid companyId,
        Func<IServiceProvider, Task> action,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves the company's default sales location (first by code).</summary>
    /// <remarks>
    /// v1 rule (ADR-145): the till sells out of the company's first location by code.
    /// Per-terminal location mapping is a later stage; a company with no location at all
    /// fails loudly rather than selling out of nowhere.
    /// </remarks>
    Task<Guid> ResolveDefaultLocationAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken = default);
}

/// <summary>Splits a mixed-basket return by origin company (ADR-128).</summary>
/// <remarks>
/// One credit (sales return) per company, each against that company's own original sale —
/// never a cross-company credit note. An application service for the same reason as the
/// completion service: returns fan out across company databases.
/// </remarks>
public interface IMixedBasketReturnService
{
    /// <summary>Returns lines from one origin company, refusing cross-company invoices.</summary>
    /// <param name="sessionId">The completed session.</param>
    /// <param name="companyId">The origin company being credited.</param>
    /// <param name="lines">The session lines to return, with quantities.</param>
    /// <param name="invoiceNumber">The invoice the caller believes it is crediting.</param>
    /// <param name="reason">Why the goods came back.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The return's id in the origin company.</returns>
    Task<Guid> ReturnLinesAsync(
        Guid sessionId,
        Guid companyId,
        IReadOnlyList<ReturnLineRequest> lines,
        string invoiceNumber,
        string reason,
        CancellationToken cancellationToken = default);
}

/// <summary>One return line: the session line and how much of it comes back.</summary>
/// <param name="SessionLineId">The trading-session line.</param>
/// <param name="QuantityValue">How much comes back.</param>
public sealed record ReturnLineRequest(Guid SessionLineId, decimal QuantityValue);
