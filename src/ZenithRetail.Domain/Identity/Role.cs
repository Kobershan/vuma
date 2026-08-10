using ZenithRetail.Domain.Entities;
using ZenithRetail.Domain.Primitives;

namespace ZenithRetail.Domain.Identity;

/// <summary>
/// A named bag of permissions — "Cashier", "Store Manager", "Buyer" (ADR-013).
/// </summary>
/// <remarks>
/// <para>
/// A role has no behaviour and no code ever branches on its name. It exists so a shop owner can
/// describe what someone may do in words they recognise, and so that changing what a cashier may do
/// is one edit rather than a hundred. The moment a code path reads a role name, renaming "Cashier"
/// to "Till Operator" breaks the product — which is why there is an architecture test.
/// </para>
/// <para>
/// Grants live in <see cref="RolePermission"/> rows rather than in a collection navigation. Adding
/// one permission to a role with forty of them should write one row, and Stage 04's sync should ship
/// one row, not re-send the whole grant list and race with a second edit.
/// </para>
/// <para>
/// Replicated <see cref="ReplicationScope.CloudToStore"/> with <see cref="ConflictPolicy.CloudWins"/>:
/// a permission grant is a security decision, and a store server compromised on a shop floor must
/// not be able to grant itself a permission and have that replicate upwards.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class Role : Entity
{
    private Role(Guid tenantId, string name, bool isSystemRole)
        : base(tenantId)
    {
        Name = name;
        NormalizedName = User.Normalize(name);
        IsSystemRole = isSystemRole;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Role()
    {
    }

    /// <summary>The name a customer sees and edits.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The upper-cased form the unique index uses.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    /// <summary>What the role is for, in a sentence, shown next to it in the admin UI.</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// True for the roles the seed creates. A system role may be renamed and re-described but not
    /// removed, because deleting the one role holding <c>identity.role.assign</c> locks the tenant out
    /// of their own installation with no way back in.
    /// </summary>
    public bool IsSystemRole { get; private set; }

    /// <summary>Creates a tenant-defined role.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="name">The role name, unique within the tenant.</param>
    /// <param name="description">What the role is for.</param>
    public static Role Create(Guid tenantId, string name, string? description = null)
        => CreateInternal(tenantId, name, description, isSystemRole: false);

    /// <summary>Creates a role the seed owns and a customer cannot delete.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="name">The role name, unique within the tenant.</param>
    /// <param name="description">What the role is for.</param>
    public static Role CreateSystemRole(Guid tenantId, string name, string? description = null)
        => CreateInternal(tenantId, name, description, isSystemRole: true);

    /// <summary>Renames and re-describes the role. Permitted on a system role.</summary>
    /// <param name="name">The new name.</param>
    /// <param name="description">The new description, or <c>null</c> to clear it.</param>
    public void SetDetails(string name, string? description)
    {
        Name = Require(name, nameof(name));
        NormalizedName = User.Normalize(Name);
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    private static Role CreateInternal(Guid tenantId, string name, string? description, bool isSystemRole)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A role must belong to a tenant.", nameof(tenantId));
        }

        Role role = new(tenantId, Require(name, nameof(name)), isSystemRole);
        role.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        return role;
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();
}
