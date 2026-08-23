using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Registry;

/// <summary><c>registry.saga_intents</c>.</summary>
internal sealed class SagaIntentConfiguration : EntityConfiguration<SagaIntent>
{
    protected override string Schema => Schemas.Registry;

    protected override string TableName => "saga_intents";

    /// <inheritdoc />
    protected override bool MapsCompanyId => false;

    protected override void ConfigureEntity(EntityTypeBuilder<SagaIntent> builder)
    {
        builder.Property(intent => intent.Kind)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(intent => intent.Payload)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(intent => intent.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(intent => intent.StartedAt);

        builder.Property(intent => intent.SettledAt);

        // The in-flight dashboard's hot query (`docs/MULTI_COMPANY.md` §9): every intent not yet
        // terminal, oldest first.
        builder.HasIndex(intent => new { intent.TenantId, intent.Status })
            .HasDatabaseName("ix_saga_intents_tenant_id_status");
    }
}
