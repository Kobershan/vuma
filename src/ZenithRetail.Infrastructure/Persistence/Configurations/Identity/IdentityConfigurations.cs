using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZenithRetail.Domain.Identity;

namespace ZenithRetail.Infrastructure.Persistence.Configurations.Identity;

/// <summary>
/// The six <c>identity</c> tables (Stage 02).
/// </summary>
/// <remarks>
/// <para>
/// Grouped in one file because they are one module's schema and all internal — the mapping of a
/// module reads as a unit, and six near-identical twenty-line files hide the one thing worth seeing,
/// which is how the tables relate. Each still derives from <see cref="EntityConfiguration{TEntity}"/>
/// and so declares only its own columns.
/// </para>
/// <para>
/// No foreign key leaves this schema. <c>Terminal.StoreId</c> points at <c>platform.stores</c> by ID
/// only, because a foreign key there would cross a module boundary and fail the architecture test
/// (ADR-010, <c>CONVENTIONS.md</c> §2).
/// </para>
/// </remarks>
internal sealed class UserConfiguration : EntityConfiguration<User>
{
    protected override string Schema => Schemas.Identity;

    protected override string TableName => "users";

    protected override void ConfigureEntity(EntityTypeBuilder<User> builder)
    {
        builder.Property(user => user.UserName).IsRequired().HasMaxLength(128);
        builder.Property(user => user.NormalizedUserName).IsRequired().HasMaxLength(128);
        builder.Property(user => user.DisplayName).IsRequired().HasMaxLength(256);
        builder.Property(user => user.Email).HasMaxLength(256);
        builder.Property(user => user.NormalizedEmail).HasMaxLength(256);
        builder.Property(user => user.PasswordHash).HasMaxLength(512);
        builder.Property(user => user.SecurityStamp).IsRequired().HasMaxLength(64);
        builder.Property(user => user.PinHash).HasMaxLength(512);
        builder.Property(user => user.FailedPasswordAttempts).IsRequired();
        builder.Property(user => user.FailedPinAttempts).IsRequired();
        builder.Property(user => user.IsActive).IsRequired();

        // Sign-in names are compared normalised, and a soft-deleted user must not reserve theirs
        // forever (§7 rule 8) — a cashier who leaves and returns keeps their own name.
        builder.HasIndex(user => new { user.TenantId, user.NormalizedUserName })
            .IsUnique()
            .HasDatabaseName("ux_users_tenant_id_normalized_user_name")
            .HasFilter("deleted_at IS NULL");

        // PIN sign-in enumerates the active PIN holders at a store, so the partial index is on the
        // predicate that query actually uses rather than on the column.
        builder.HasIndex(user => user.TenantId)
            .HasDatabaseName("ix_users_tenant_id_pin")
            .HasFilter("pin_hash IS NOT NULL AND deleted_at IS NULL");
    }
}

/// <summary><c>identity.roles</c>.</summary>
internal sealed class RoleConfiguration : EntityConfiguration<Role>
{
    protected override string Schema => Schemas.Identity;

    protected override string TableName => "roles";

    protected override void ConfigureEntity(EntityTypeBuilder<Role> builder)
    {
        builder.Property(role => role.Name).IsRequired().HasMaxLength(128);
        builder.Property(role => role.NormalizedName).IsRequired().HasMaxLength(128);
        builder.Property(role => role.Description).HasMaxLength(512);
        builder.Property(role => role.IsSystemRole).IsRequired();

        builder.HasIndex(role => new { role.TenantId, role.NormalizedName })
            .IsUnique()
            .HasDatabaseName("ux_roles_tenant_id_normalized_name")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>identity.role_permissions</c>.</summary>
internal sealed class RolePermissionConfiguration : EntityConfiguration<RolePermission>
{
    protected override string Schema => Schemas.Identity;

    protected override string TableName => "role_permissions";

    protected override void ConfigureEntity(EntityTypeBuilder<RolePermission> builder)
    {
        builder.Property(grant => grant.RoleId).IsRequired();

        builder.Property(grant => grant.Permission)
            .IsRequired()
            .HasMaxLength(Domain.Identity.PermissionKey.MaxLength);

        // Ignored rather than mapped: Key is a projection of the stored string, and a computed column
        // holding a parsed copy of another column is two things that can disagree.
        builder.Ignore(grant => grant.Key);

        builder.HasIndex(grant => new { grant.RoleId, grant.Permission })
            .IsUnique()
            .HasDatabaseName("ux_role_permissions_role_id_permission")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>identity.user_role_assignments</c>.</summary>
internal sealed class UserRoleAssignmentConfiguration : EntityConfiguration<UserRoleAssignment>
{
    protected override string Schema => Schemas.Identity;

    protected override string TableName => "user_role_assignments";

    protected override void ConfigureEntity(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        builder.Property(assignment => assignment.UserId).IsRequired();
        builder.Property(assignment => assignment.RoleId).IsRequired();
        builder.Ignore(assignment => assignment.IsTenantWide);

        // AreNullsDistinct(false) — PostgreSQL 15's NULLS NOT DISTINCT. store_id is null for a
        // tenant-wide assignment, and under the SQL default two nulls are distinct, so the same
        // tenant-wide role could be assigned to one user any number of times.
        builder.HasIndex(assignment => new { assignment.UserId, assignment.RoleId, assignment.StoreId })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ux_user_role_assignments_user_id_role_id_store_id")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>identity.terminals</c>.</summary>
internal sealed class TerminalConfiguration : EntityConfiguration<Terminal>
{
    protected override string Schema => Schemas.Identity;

    protected override string TableName => "terminals";

    protected override void ConfigureEntity(EntityTypeBuilder<Terminal> builder)
    {
        builder.Property(terminal => terminal.Code).IsRequired().HasMaxLength(16);
        builder.Property(terminal => terminal.Name).IsRequired().HasMaxLength(128);

        // Stored as text (docs/DATA_MODEL.md §2): an enum persisted by ordinal turns a reordered
        // member into silently relabelled history.
        builder.Property(terminal => terminal.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(terminal => terminal.EnrolmentCodeHash).HasMaxLength(512);
        builder.Property(terminal => terminal.CertificateThumbprint).HasMaxLength(64).IsFixedLength();
        builder.Property(terminal => terminal.DeviceFingerprint).HasMaxLength(256);
        builder.Property(terminal => terminal.HasFingerprintDrift).IsRequired();
        builder.Property(terminal => terminal.RevocationReason).HasMaxLength(256);
        builder.Ignore(terminal => terminal.CanAuthenticate);

        builder.HasIndex(terminal => new { terminal.StoreId, terminal.Code })
            .IsUnique()
            .HasDatabaseName("ux_terminals_store_id_code")
            .HasFilter("deleted_at IS NULL");

        // A thumbprint identifies exactly one terminal across the whole installation — it is what a
        // client certificate is resolved through before any tenant is known, so a collision would be
        // one store's till authenticating as another's.
        builder.HasIndex(terminal => terminal.CertificateThumbprint)
            .IsUnique()
            .HasDatabaseName("ux_terminals_certificate_thumbprint")
            .HasFilter("certificate_thumbprint IS NOT NULL AND deleted_at IS NULL");
    }
}

/// <summary><c>identity.refresh_tokens</c>.</summary>
internal sealed class RefreshTokenConfiguration : EntityConfiguration<RefreshToken>
{
    protected override string Schema => Schemas.Identity;

    protected override string TableName => "refresh_tokens";

    protected override void ConfigureEntity(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.Property(token => token.UserId).IsRequired();
        builder.Property(token => token.TokenHash).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.Property(token => token.SecurityStamp).IsRequired().HasMaxLength(64);
        builder.Property(token => token.IssuedAt).IsRequired();
        builder.Property(token => token.ExpiresAt).IsRequired();

        builder.Property(token => token.RevocationReason)
            .HasConversion<string>()
            .HasMaxLength(32);

        // The refresh path looks a token up by digest and by nothing else, so this is the index that
        // matters. Unique because two rows sharing a digest would mean two live sessions that rotate
        // over each other.
        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_refresh_tokens_token_hash");

        builder.HasIndex(token => token.UserId)
            .HasDatabaseName("ix_refresh_tokens_user_id");
    }
}
