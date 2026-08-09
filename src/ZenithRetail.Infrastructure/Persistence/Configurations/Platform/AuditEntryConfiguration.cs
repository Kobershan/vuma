using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZenithRetail.Domain.Platform;

namespace ZenithRetail.Infrastructure.Persistence.Configurations.Platform;

/// <summary><c>platform.audit_entries</c> — append-only (R6).</summary>
internal sealed class AuditEntryConfiguration : EntityConfiguration<AuditEntry>
{
    protected override string Schema => Schemas.Platform;

    protected override string TableName => "audit_entries";

    protected override void ConfigureEntity(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.Property(entry => entry.EntityType)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(entry => entry.TableName)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(entry => entry.EntityId)
            .IsRequired();

        // Stored as text, not as the underlying int. An enum persisted by ordinal turns a reordered
        // enum member into silently relabelled history, and history is the one thing this table has.
        builder.Property(entry => entry.Action)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(entry => entry.Principal)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(entry => entry.TerminalId);

        builder.Property(entry => entry.IsSystemAction)
            .IsRequired();

        builder.Property(entry => entry.OccurredAt)
            .IsRequired();

        builder.Property(entry => entry.Changes)
            .IsRequired()
            .HasColumnType("jsonb");

        // The investigation query is always "what happened to this row", so it gets the index. The
        // second one serves "what did this person do that afternoon", which is the other half of R6.
        builder.HasIndex(entry => new { entry.TenantId, entry.EntityType, entry.EntityId })
            .HasDatabaseName("ix_audit_entries_tenant_id_entity_type_entity_id");

        builder.HasIndex(entry => new { entry.TenantId, entry.OccurredAt })
            .HasDatabaseName("ix_audit_entries_tenant_id_occurred_at");
    }
}
