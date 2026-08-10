using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Identity.Permissions;

namespace VumaRetail.Application.Identity.Queries;

/// <summary>What a user may do in a store.</summary>
/// <param name="UserId">The user.</param>
/// <param name="StoreId">The store being acted in, or <c>null</c> for a tenant-wide action.</param>
public sealed record GetEffectivePermissionsQuery(Guid UserId, Guid? StoreId = null)
    : IQuery<IReadOnlyCollection<string>>;

/// <summary>
/// Resolves the union of a user's tenant-wide roles and their roles scoped to the given store.
/// </summary>
/// <remarks>
/// <para>
/// This is what <c>GET /api/v1/me/permissions</c> returns and what the Android app drives its
/// navigation from (ADR-013). It is asked <em>per store</em>, never once and cached across stores: a
/// supervisor who may approve refunds at the airport branch does not thereby approve them at head
/// office, and a cached union across stores is exactly how that boundary gets lost.
/// </para>
/// <para>
/// Permissions the catalogue no longer declares are dropped rather than returned. A module switched
/// off, or renamed between releases, leaves stale grants in a customer's database; returning them
/// would let a client show a button that the server will refuse.
/// </para>
/// </remarks>
/// <param name="roles">Role and assignment lookup.</param>
/// <param name="catalogue">The closed set of permissions this installation understands.</param>
public sealed class GetEffectivePermissionsQueryHandler(IRoleRepository roles, IPermissionCatalogue catalogue)
    : IQueryHandler<GetEffectivePermissionsQuery, IReadOnlyCollection<string>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> HandleAsync(
        GetEffectivePermissionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyCollection<string> granted = await roles
            .ListEffectivePermissionsAsync(query.UserId, query.StoreId, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. granted
                .Where(permission => Domain.Identity.PermissionKey.TryParse(permission, out Domain.Identity.PermissionKey key)
                    && catalogue.Contains(key))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(permission => permission, StringComparer.Ordinal),
        ];
    }
}
