using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Partners;
using VumaRetail.Domain.Partners;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IPartnerRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class PartnerRepository(VumaRetailDbContext context) : IPartnerRepository
{
    /// <inheritdoc />
    public Task<Partner?> FindAsync(Guid partnerId, CancellationToken cancellationToken = default)
        => context.Partners.FirstOrDefaultAsync(partner => partner.Id == partnerId, cancellationToken);

    /// <inheritdoc />
    public Task<Partner?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalized = code.Trim().ToUpperInvariant();

        return context.Partners.FirstOrDefaultAsync(partner => partner.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Partner> Partners, bool HasMore)> ListPageAsync(
        KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Partner> query = context.Partners.AsNoTracking();

        if (after is { } cursor)
        {
            // Keyset, not offset (docs/API_STANDARDS.md §8) — see ItemRepository.ListPageAsync for why.
            query = query.Where(partner => partner.Code.CompareTo(cursor.SortKey) > 0
                || (partner.Code == cursor.SortKey && partner.Id.CompareTo(cursor.Id) > 0));
        }

        List<Partner> page = await query
            .OrderBy(partner => partner.Code)
            .ThenBy(partner => partner.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool hasMore = page.Count > limit;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return (page, hasMore);
    }

    /// <inheritdoc />
    public void Add(Partner partner) => context.Partners.Add(partner);
}
