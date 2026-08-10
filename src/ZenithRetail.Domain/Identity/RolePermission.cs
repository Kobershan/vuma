using ZenithRetail.Domain.Entities;
using ZenithRetail.Domain.Primitives;

namespace ZenithRetail.Domain.Identity;

/// <summary>
/// One permission granted to one role.
/// </summary>
/// <remarks>
/// A row per grant rather than a collection on <see cref="Role"/>: adding one permission to a role
/// that has forty writes one row, syncs one row, and audits as one change naming the permission that
/// changed — which is exactly the question asked afterwards. Whether the permission is one a module
/// actually declares is checked by the application layer against the catalogue (ADR-013); the domain
/// only guarantees the string is shaped like a permission.
/// </remarks>
[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class RolePermission : Entity
{
    private RolePermission(Guid tenantId, Guid roleId, PermissionKey permission)
        : base(tenantId)
    {
        RoleId = roleId;
        Permission = permission.Value;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private RolePermission()
    {
    }

    /// <summary>The role the permission is granted to.</summary>
    public Guid RoleId { get; private set; }

    /// <summary>The permission, as its <c>module.entity.action</c> string.</summary>
    public string Permission { get; private set; } = string.Empty;

    /// <summary>The permission as a parsed key.</summary>
    public PermissionKey Key => PermissionKey.Parse(Permission);

    /// <summary>Grants a permission to a role.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="roleId">The role receiving the grant.</param>
    /// <param name="permission">The permission being granted.</param>
    public static RolePermission Grant(Guid tenantId, Guid roleId, PermissionKey permission)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A grant must belong to a tenant.", nameof(tenantId));
        }

        if (roleId == Guid.Empty)
        {
            throw new ArgumentException("A grant must name a role.", nameof(roleId));
        }

        return new RolePermission(tenantId, roleId, permission);
    }
}
