using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Registry;

/// <summary><c>registry.outbox_messages</c>.</summary>
internal sealed class RegistryOutboxMessageConfiguration : EntityConfiguration<RegistryOutboxMessage>
{
    protected override string Schema => Schemas.Registry;

    protected override string TableName => "outbox_messages";

    protected override void ConfigureEntity(EntityTypeBuilder<RegistryOutboxMessage> builder)
    {
        builder.Property(message => message.OperationId)
            .IsRequired();

        builder.Property(message => message.MessageKind)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(message => message.TargetCompanyId);

        builder.Property(message => message.Payload)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(message => message.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(message => message.AttemptCount)
            .IsRequired();

        builder.Property(message => message.NextAttemptAt);

        builder.Property(message => message.DispatchedAt);

        builder.Property(message => message.LastError)
            .HasMaxLength(RegistryOutboxMessage.MaxErrorLength);

        builder.HasIndex(message => message.OperationId)
            .IsUnique()
            .HasDatabaseName("ux_outbox_messages_operation_id");

        // The dispatcher's hot query (mirrors ix_{table}_sync_state on every other entity): what is
        // still pending, excluding the overwhelming majority that already dispatched.
        builder.HasIndex(message => message.Status)
            .HasDatabaseName("ix_outbox_messages_status")
            .HasFilter("status <> 'Dispatched'");
    }
}
