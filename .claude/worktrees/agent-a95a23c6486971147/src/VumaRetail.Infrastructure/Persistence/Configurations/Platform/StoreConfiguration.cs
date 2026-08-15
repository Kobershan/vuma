using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Platform;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Platform;

/// <summary><c>platform.stores</c>.</summary>
internal sealed class StoreConfiguration : EntityConfiguration<Store>
{
    protected override string Schema => Schemas.Platform;

    protected override string TableName => "stores";

    protected override void ConfigureEntity(EntityTypeBuilder<Store> builder)
    {
        builder.Property(store => store.Code)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(store => store.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(store => store.Address)
            .HasMaxLength(512);

        builder.Property(store => store.CurrencyOverride)
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(store => store.TimeZoneOverride)
            .HasMaxLength(64);

        builder.Property(store => store.IsActive)
            .IsRequired();

        // Store codes appear on receipts and in document number series, so two live stores sharing
        // one would produce colliding document numbers — which is also one of the abuse signals
        // ADR-026 looks for. Unique within a tenant, and only among live rows, because a soft-deleted
        // store must not reserve its code forever (§7 rule 8).
        builder.HasIndex(store => new { store.TenantId, store.Code })
            .IsUnique()
            .HasDatabaseName("ux_stores_tenant_id_code")
            .HasFilter("deleted_at IS NULL");
    }
}
