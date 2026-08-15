using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Partners;

namespace VumaRetail.Application.Partners;

/// <summary>Reads and writes <see cref="Partner"/> rows.</summary>
/// <remarks>
/// Repositories return tracked entities and never commit. The unit of work is the pipeline's
/// (<c>IUnitOfWork</c>, CLAUDE.md §7 rule 2).
/// </remarks>
public interface IPartnerRepository
{
    /// <summary>Finds a partner by id.</summary>
    Task<Partner?> FindAsync(Guid partnerId, CancellationToken cancellationToken = default);

    /// <summary>Finds a partner by its code, case-insensitively.</summary>
    Task<Partner?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// A keyset page of partners ordered by code, then id. See <c>docs/API_STANDARDS.md</c> §8.
    /// </summary>
    /// <param name="after">The cursor of the last row the caller already has, or <c>null</c> for the first page.</param>
    /// <param name="limit">How many rows to return, already clamped by the query handler.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<(IReadOnlyList<Partner> Partners, bool HasMore)> ListPageAsync(
        KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new partner.</summary>
    void Add(Partner partner);
}
