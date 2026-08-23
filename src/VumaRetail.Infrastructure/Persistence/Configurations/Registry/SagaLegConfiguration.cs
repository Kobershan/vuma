using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Registry;

/// <summary><c>registry.saga_legs</c>.</summary>
internal sealed class SagaLegConfiguration : EntityConfiguration<SagaLeg>
{
    protected override string Schema => Schemas.Registry;

    protected override string TableName => "saga_legs";

    /// <inheritdoc />
    /// <remarks>
    /// A leg's own <see cref="SagaLeg.TargetCompanyId"/> already answers "which company"; the
    /// inherited <c>CompanyId</c> would be a second, always-empty column with a confusingly similar
    /// name sitting right next to it.
    /// </remarks>
    protected override bool MapsCompanyId => false;

    protected override void ConfigureEntity(EntityTypeBuilder<SagaLeg> builder)
    {
        builder.Property(leg => leg.IntentId)
            .IsRequired();

        builder.Property(leg => leg.LegKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(leg => leg.TargetCompanyId)
            .IsRequired();

        builder.Property(leg => leg.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(leg => leg.AttemptCount)
            .IsRequired();

        builder.Property(leg => leg.AcknowledgedAt);

        builder.Property(leg => leg.LastError)
            .HasMaxLength(SagaLeg.MaxErrorLength);

        // ADR-116: every leg is idempotent, keyed by (intent_id, leg_id). This is that key, and it is
        // what makes a retried dispatch a no-op rather than a duplicate. Not filtered to live rows —
        // a leg is never soft-deleted, only moved through its status machine.
        builder.HasIndex(leg => new { leg.IntentId, leg.LegKey })
            .IsUnique()
            .HasDatabaseName("ux_saga_legs_intent_id_leg_key");

        // The unapplied-legs report's hot query (`docs/MULTI_COMPANY.md` §7): every leg outstanding for
        // one company.
        builder.HasIndex(leg => new { leg.TargetCompanyId, leg.Status })
            .HasDatabaseName("ix_saga_legs_target_company_id_status");
    }
}
