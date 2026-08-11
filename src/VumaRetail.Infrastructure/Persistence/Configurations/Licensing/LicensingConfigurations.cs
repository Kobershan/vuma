using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Licensing;

/// <summary><c>licensing.activations</c> — this installation's binding (<c>LICENSING.md</c> §3).</summary>
internal sealed class ActivationConfiguration : EntityConfiguration<Activation>
{
    protected override string Schema => Schemas.Licensing;

    protected override string TableName => "activations";

    protected override void ConfigureEntity(EntityTypeBuilder<Activation> builder)
    {
        // A SHA-256 digest, never the key. A licence key is a bearer credential for one installation
        // and belongs in the database on the same terms as a password (docs/SECURITY.md §1).
        builder.Property(activation => activation.LicenceKeyDigest).IsRequired().HasMaxLength(64);

        builder.Property(activation => activation.ActivationReference).IsRequired();
        builder.Property(activation => activation.InstallId).IsRequired();
        builder.Property(activation => activation.NodeId).IsRequired().HasMaxLength(HlcStamp.NodeIdMaxLength);
        builder.Property(activation => activation.FingerprintSalt).IsRequired().HasMaxLength(64);

        // jsonb rather than text: the per-component hashes are read as a map on every rebind score,
        // and the shape is worth keeping queryable when somebody asks why a machine stopped matching.
        builder.Property(activation => activation.FingerprintComponents)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(activation => activation.FingerprintDigest).IsRequired().HasMaxLength(64);

        builder.Property(activation => activation.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(activation => activation.ActivatedAt).IsRequired();
        builder.Property(activation => activation.RebindCount).IsRequired();

        // One live activation per node. Two would mean two answers to "is this machine licensed", and
        // the enforcement policy would take whichever the database returned first.
        builder.HasIndex(activation => new { activation.TenantId, activation.NodeId })
            .IsUnique()
            .HasDatabaseName("ux_activations_tenant_node")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>licensing.licences</c> — the signed monthly licences.</summary>
internal sealed class LicenceConfiguration : EntityConfiguration<Licence>
{
    protected override string Schema => Schemas.Licensing;

    protected override string TableName => "licences";

    protected override void ConfigureEntity(EntityTypeBuilder<Licence> builder)
    {
        builder.Property(licence => licence.ActivationId).IsRequired();

        // No length cap. The document is the signed truth and a future licence may carry more
        // entitlements than this one; truncating it would produce a row that fails verification.
        builder.Property(licence => licence.Document).IsRequired();

        builder.Property(licence => licence.PlanCode).IsRequired().HasMaxLength(64);
        builder.Property(licence => licence.Entitlements).IsRequired().HasColumnType("jsonb");
        builder.Property(licence => licence.Limits).IsRequired().HasColumnType("jsonb");
        builder.Property(licence => licence.IssuedAt).IsRequired();
        builder.Property(licence => licence.ExpiresAt).IsRequired();
        builder.Property(licence => licence.FingerprintDigest).IsRequired().HasMaxLength(64);
        builder.Property(licence => licence.Nonce).IsRequired().HasMaxLength(64);
        builder.Property(licence => licence.IssuanceCounter).IsRequired();

        // The two queries this table has: "what is current" and "what is the highest counter we have
        // ever seen" — the second being what makes a replayed old licence useless.
        builder.HasIndex(licence => new { licence.TenantId, licence.IssuanceCounter })
            .HasDatabaseName("ix_licences_tenant_issuance_counter");
    }
}

/// <summary><c>licensing.leases</c> — the short-lived tokens the software runs on.</summary>
internal sealed class LeaseConfiguration : EntityConfiguration<Lease>
{
    protected override string Schema => Schemas.Licensing;

    protected override string TableName => "leases";

    protected override void ConfigureEntity(EntityTypeBuilder<Lease> builder)
    {
        builder.Property(lease => lease.ActivationId).IsRequired();
        builder.Property(lease => lease.LeaseReference).IsRequired();
        builder.Property(lease => lease.Document).IsRequired();
        builder.Property(lease => lease.Entitlements).IsRequired().HasColumnType("jsonb");
        builder.Property(lease => lease.Limits).IsRequired().HasColumnType("jsonb");

        builder.Property(lease => lease.DeclaredLevel)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(lease => lease.DeclaredReason)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(lease => lease.IssuedAt).IsRequired();
        builder.Property(lease => lease.ExpiresAt).IsRequired();
        builder.Property(lease => lease.IssuanceCounter).IsRequired();

        // §7 rule 4: money is decimal(18,4) with an explicit currency, even when it is the vendor's
        // money rather than the tenant's. This one never reaches a ledger (ADR-025) and still obeys
        // the rule, because a column that is exempt from it is a column somebody will reuse.
        builder.Property(lease => lease.AmountDueValue).HasColumnType("decimal(18,4)");
        builder.Property(lease => lease.AmountDueCurrency).HasMaxLength(3).IsFixedLength();

        builder.Property(lease => lease.PayUrl).HasMaxLength(512);
        builder.Property(lease => lease.UpdatePaymentMethodUrl).HasMaxLength(512);
        builder.Property(lease => lease.SupportPhone).HasMaxLength(64);
        builder.Property(lease => lease.Messages).IsRequired().HasColumnType("jsonb");

        // "What is the current lease" — asked on every command that writes, so it is indexed rather
        // than sorted.
        builder.HasIndex(lease => new { lease.TenantId, lease.IssuedAt })
            .HasDatabaseName("ix_leases_tenant_issued_at");
    }
}

/// <summary><c>licensing.emergency_unlocks</c> — codes redeemed here (<c>LICENSING.md</c> §5).</summary>
internal sealed class EmergencyUnlockConfiguration : EntityConfiguration<EmergencyUnlock>
{
    protected override string Schema => Schemas.Licensing;

    protected override string TableName => "emergency_unlocks";

    protected override void ConfigureEntity(EntityTypeBuilder<EmergencyUnlock> builder)
    {
        builder.Property(unlock => unlock.CodeReference).IsRequired();
        builder.Property(unlock => unlock.RedeemedAt).IsRequired();
        builder.Property(unlock => unlock.ExpiresAt).IsRequired();
        builder.Property(unlock => unlock.Reason).IsRequired().HasMaxLength(256);

        // Single-use, and this index is the guarantee rather than the handler's check. Deliberately
        // not filtered on deleted_at: a soft-deleted redemption must still block a replay, exactly as
        // the inbox's does for a replayed sync operation.
        builder.HasIndex(unlock => new { unlock.TenantId, unlock.CodeReference })
            .IsUnique()
            .HasDatabaseName("ux_emergency_unlocks_tenant_code");

        // "Is one in force right now", asked by every enforcement decision.
        builder.HasIndex(unlock => new { unlock.TenantId, unlock.ExpiresAt })
            .HasDatabaseName("ix_emergency_unlocks_tenant_expires_at");
    }
}

/// <summary><c>licensing.tamper_flags</c> — what the hardening noticed (<c>LICENSING.md</c> §7).</summary>
internal sealed class TamperFlagConfiguration : EntityConfiguration<TamperFlag>
{
    protected override string Schema => Schemas.Licensing;

    protected override string TableName => "tamper_flags";

    protected override void ConfigureEntity(EntityTypeBuilder<TamperFlag> builder)
    {
        builder.Property(flag => flag.Kind).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(flag => flag.DetectedAt).IsRequired();
        builder.Property(flag => flag.Detail).IsRequired().HasMaxLength(TamperFlag.MaxDetailLength);

        // What the heartbeat asks: what has not been reported yet. Partial, because reported flags are
        // history and history is most of the table.
        builder.HasIndex(flag => new { flag.TenantId, flag.DetectedAt })
            .HasDatabaseName("ix_tamper_flags_unreported")
            .HasFilter("reported_at IS NULL");
    }
}

/// <summary><c>licensing.clock_watermarks</c> — the highest instant ever seen here.</summary>
internal sealed class ClockWatermarkConfiguration : EntityConfiguration<ClockWatermark>
{
    protected override string Schema => Schemas.Licensing;

    protected override string TableName => "clock_watermarks";

    protected override void ConfigureEntity(EntityTypeBuilder<ClockWatermark> builder)
    {
        builder.Property(watermark => watermark.NodeId)
            .IsRequired()
            .HasMaxLength(HlcStamp.NodeIdMaxLength);

        builder.Property(watermark => watermark.HighestSeen).IsRequired();
        builder.Property(watermark => watermark.RollbackCount).IsRequired();

        // One per node. Two watermarks would mean two answers to "what is the latest we have seen",
        // and the lower one would be a free extension to anybody who found it.
        builder.HasIndex(watermark => new { watermark.TenantId, watermark.NodeId })
            .IsUnique()
            .HasDatabaseName("ux_clock_watermarks_tenant_node")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>licensing.metering_records</c> — the daily rollups (<c>LICENSING.md</c> §9).</summary>
internal sealed class MeteringRecordConfiguration : EntityConfiguration<MeteringRecord>
{
    protected override string Schema => Schemas.Licensing;

    protected override string TableName => "metering_records";

    protected override void ConfigureEntity(EntityTypeBuilder<MeteringRecord> builder)
    {
        builder.Property(record => record.NodeId).IsRequired().HasMaxLength(HlcStamp.NodeIdMaxLength);
        builder.Property(record => record.Period).IsRequired().HasColumnType("date");
        builder.Property(record => record.Payload).IsRequired().HasColumnType("jsonb");

        builder.Property(record => record.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(record => record.AttemptCount).IsRequired();

        // Idempotency by (node, period) — the contract in docs/API_CONTROL_PLANE.md §2. A store that
        // has been offline for six weeks catches up without billing itself twice.
        builder.HasIndex(record => new { record.TenantId, record.NodeId, record.Period })
            .IsUnique()
            .HasDatabaseName("ux_metering_records_tenant_node_period")
            .HasFilter("deleted_at IS NULL");

        // The delivery pass's query: what is still waiting, oldest first.
        builder.HasIndex(record => new { record.TenantId, record.Period })
            .HasDatabaseName("ix_metering_records_pending")
            .HasFilter("state = 'Pending' AND deleted_at IS NULL");
    }
}

/// <summary><c>licensing.support_grants</c> — tenant-granted vendor access (<c>LICENSING.md</c> §9).</summary>
internal sealed class SupportGrantConfiguration : EntityConfiguration<SupportGrant>
{
    protected override string Schema => Schemas.Licensing;

    protected override string TableName => "support_grants";

    protected override void ConfigureEntity(EntityTypeBuilder<SupportGrant> builder)
    {
        builder.Property(grant => grant.GrantReference).IsRequired();
        builder.Property(grant => grant.RequestedBy).IsRequired().HasMaxLength(128);
        builder.Property(grant => grant.Reason).IsRequired().HasMaxLength(512);
        builder.Property(grant => grant.Scope).IsRequired().HasMaxLength(128);
        builder.Property(grant => grant.RequestedAt).IsRequired();

        builder.Property(grant => grant.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(grant => grant.DecidedBy).HasMaxLength(128);

        // A heartbeat is at-least-once, so a duplicated request must not become two approval prompts
        // for one support visit.
        builder.HasIndex(grant => new { grant.TenantId, grant.GrantReference })
            .IsUnique()
            .HasDatabaseName("ux_support_grants_tenant_reference")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(grant => new { grant.TenantId, grant.RequestedAt })
            .HasDatabaseName("ix_support_grants_tenant_requested_at");
    }
}
