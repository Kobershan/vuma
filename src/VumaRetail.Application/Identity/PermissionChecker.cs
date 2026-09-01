using VumaRetail.Application.Abstractions.Identity;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Identity;

/// <summary>
/// Answers <see cref="IPermissionChecker"/> from the same role lookup and catalogue filter
/// <c>GetEffectivePermissionsQueryHandler</c> uses for <c>GET /api/v1/me/permissions</c>.
/// </summary>
/// <param name="roles">Role and assignment lookup.</param>
/// <param name="catalogue">The closed set of permissions this installation understands.</param>
public sealed class PermissionChecker(IRoleRepository roles, IPermissionCatalogue catalogue) : IPermissionChecker
{
    /// <inheritdoc />
    public async Task<bool> HasPermissionAsync(
        Guid userId, Guid? storeId, string permission, CancellationToken cancellationToken = default)
    {
        // A permission nobody currently declares is never held, whatever a stale role grant says — the
        // same rule GetEffectivePermissionsQueryHandler applies to what it hands back to a client.
        if (!PermissionKey.TryParse(permission, out PermissionKey key) || !catalogue.Contains(key))
        {
            return false;
        }

        IReadOnlyCollection<string> granted = await roles
            .ListEffectivePermissionsAsync(userId, storeId, cancellationToken)
            .ConfigureAwait(false);

        return granted.Contains(permission, StringComparer.Ordinal);
    }
}
