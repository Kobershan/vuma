using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Partners;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Partners.Queries;

/// <summary>A partner as read back out of the register. Application's own shape (§7 rule 1).</summary>
/// <param name="Id">The partner's id.</param>
/// <param name="Code">The partner's code.</param>
/// <param name="Name">The partner's trading name.</param>
/// <param name="Type">Customer, supplier, or both.</param>
/// <param name="Address">The partner's address, or <c>null</c>.</param>
/// <param name="Email">The partner's email, or <c>null</c>.</param>
/// <param name="Phone">The partner's phone number, or <c>null</c>.</param>
/// <param name="TaxNumber">The partner's tax number, or <c>null</c>.</param>
/// <param name="IsActive">Whether the partner may still be traded with.</param>
public sealed record PartnerResult(
    Guid Id,
    string Code,
    string Name,
    PartnerType Type,
    Address? Address,
    string? Email,
    string? Phone,
    string? TaxNumber,
    bool IsActive);

/// <summary>Reads one partner by id.</summary>
/// <param name="PartnerId">The partner.</param>
public sealed record GetPartnerQuery(Guid PartnerId) : IQuery<PartnerResult>;

/// <summary>Reads a partner.</summary>
/// <param name="partners">Partner lookup.</param>
public sealed class GetPartnerQueryHandler(IPartnerRepository partners)
    : IQueryHandler<GetPartnerQuery, PartnerResult>
{
    /// <inheritdoc />
    public async Task<PartnerResult> HandleAsync(GetPartnerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Partner partner = await partners.FindAsync(query.PartnerId, cancellationToken).ConfigureAwait(false)
            ?? throw new PartnerNotFoundException("partner", query.PartnerId);

        return ToResult(partner);
    }

    internal static PartnerResult ToResult(Partner partner) => new(
        partner.Id,
        partner.Code,
        partner.Name,
        partner.Type,
        partner.Address,
        partner.Email,
        partner.Phone,
        partner.TaxNumber,
        partner.IsActive);
}

/// <summary>
/// A keyset-paginated page of partners, ordered by code (<c>docs/API_STANDARDS.md</c> §8).
/// </summary>
/// <param name="Limit">How many rows to return. Clamped to <see cref="Paging.MaxLimit"/>.</param>
/// <param name="After">The opaque cursor of the last row the caller already has, or <c>null</c> for the first page.</param>
public sealed record ListPartnersQuery(int? Limit = null, string? After = null) : IQuery<PageResult<PartnerResult>>;

/// <summary>Lists partners a page at a time.</summary>
/// <param name="partners">Partner lookup.</param>
public sealed class ListPartnersQueryHandler(IPartnerRepository partners)
    : IQueryHandler<ListPartnersQuery, PageResult<PartnerResult>>
{
    /// <inheritdoc />
    public async Task<PageResult<PartnerResult>> HandleAsync(
        ListPartnersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);
        KeysetCursor? after = KeysetCursor.TryDecode(query.After);

        (IReadOnlyList<Partner> page, bool hasMore) = await partners
            .ListPageAsync(after, limit, cancellationToken)
            .ConfigureAwait(false);

        string? nextCursor = hasMore && page.Count > 0
            ? new KeysetCursor(page[^1].Code, page[^1].Id).Encode()
            : null;

        return new PageResult<PartnerResult>(
            [.. page.Select(GetPartnerQueryHandler.ToResult)],
            nextCursor,
            hasMore);
    }
}
