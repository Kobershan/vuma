using ZenithRetail.Domain.Entities;
using ZenithRetail.Domain.Primitives;

namespace ZenithRetail.Domain.Identity;

/// <summary>
/// A user holds a role, either across the whole tenant or in one store (ADR-013).
/// </summary>
/// <remarks>
/// <para>
/// The scope is the base entity's own <c>store_id</c>: <c>null</c> means tenant-wide, a value means
/// that store only. A second "scope_store_id" column would be a column that can disagree with the
/// one every other table already uses, and reconciling those two after a year of trading is not a
/// migration anybody wants to write.
/// </para>
/// <para>
/// Scoping is what makes R8 real for permissions. A supervisor who may approve refunds at the airport
/// branch does not thereby approve them at head office, and the effective-permission query is always
/// asked about a specific store for that reason.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class UserRoleAssignment : Entity
{
    private UserRoleAssignment(Guid tenantId, Guid userId, Guid roleId, Guid? storeId)
        : base(tenantId, storeId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private UserRoleAssignment()
    {
    }

    /// <summary>The user holding the role.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The role held.</summary>
    public Guid RoleId { get; private set; }

    /// <summary>True when the assignment applies in every store, rather than in one.</summary>
    public bool IsTenantWide => StoreId is null;

    /// <summary>Assigns a role to a user.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="userId">The user receiving the role.</param>
    /// <param name="roleId">The role being assigned.</param>
    /// <param name="storeId">The store the assignment is scoped to, or <c>null</c> for tenant-wide.</param>
    public static UserRoleAssignment Assign(Guid tenantId, Guid userId, Guid roleId, Guid? storeId = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An assignment must belong to a tenant.", nameof(tenantId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("An assignment must name a user.", nameof(userId));
        }

        if (roleId == Guid.Empty)
        {
            throw new ArgumentException("An assignment must name a role.", nameof(roleId));
        }

        return new UserRoleAssignment(tenantId, userId, roleId, storeId);
    }

    /// <summary>True when this assignment grants its role in the given store.</summary>
    /// <param name="storeId">The store being acted in, or <c>null</c> for a tenant-wide action.</param>
    /// <returns>Whether the assignment applies.</returns>
    public bool AppliesIn(Guid? storeId) => IsTenantWide || StoreId == storeId;
}
