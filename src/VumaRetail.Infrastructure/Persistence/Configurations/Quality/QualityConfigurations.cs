using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Quality;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Quality;

internal sealed class QualityHoldConfiguration : EntityConfiguration<QualityHold>
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    protected override string Schema => Schemas.Quality;
    protected override string TableName => "quality_holds";

    protected override void ConfigureEntity(EntityTypeBuilder<QualityHold> builder)
    {
        builder.Property(hold => hold.CompanyId).IsRequired();
        builder.Property(hold => hold.OperationId).IsRequired();
        builder.Property(hold => hold.LocationId).IsRequired();
        builder.Property(hold => hold.ItemId);
        builder.Property(hold => hold.ItemVariantId);
        builder.Property(hold => hold.Quantity).HasColumnType("jsonb").IsRequired().HasConversion(
            quantity => JsonSerializer.Serialize(quantity, SerializerOptions),
            json => JsonSerializer.Deserialize<Quantity>(json, SerializerOptions));
        builder.Property(hold => hold.Reason).IsRequired().HasMaxLength(256);
        builder.Property(hold => hold.ReservationId).IsRequired();
        builder.Property(hold => hold.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(hold => hold.DispositionReason).HasMaxLength(256);
        builder.HasIndex(hold => new { hold.TenantId, hold.OperationId }).IsUnique()
            .HasDatabaseName("ux_quality_holds_tenant_operation")
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(hold => new { hold.TenantId, hold.CompanyId, hold.Status })
            .HasDatabaseName("ix_quality_holds_tenant_company_status")
            .HasFilter("deleted_at IS NULL");
    }
}
