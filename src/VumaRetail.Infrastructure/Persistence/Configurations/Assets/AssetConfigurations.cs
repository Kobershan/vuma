using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Assets;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Assets;

internal sealed class FixedAssetConfiguration : EntityConfiguration<FixedAsset>
{
    protected override string Schema => Schemas.Assets;
    protected override string TableName => "fixed_assets";
    protected override void ConfigureEntity(EntityTypeBuilder<FixedAsset> builder)
    {
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.AssetNumber).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Description).IsRequired().HasMaxLength(256);
        builder.Property(x => x.AcquiredOn).IsRequired();
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.HasMoney(x => x.Cost, "cost");
        builder.HasIndex(x => new { x.TenantId, x.CompanyId, x.AssetNumber }).IsUnique()
            .HasDatabaseName("ux_fixed_assets_tenant_company_number").HasFilter("deleted_at IS NULL");
    }
}

internal sealed class AssetBookConfiguration : EntityConfiguration<AssetBook>
{
    protected override string Schema => Schemas.Assets;
    protected override string TableName => "asset_books";
    protected override void ConfigureEntity(EntityTypeBuilder<AssetBook> builder)
    {
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.AssetId).IsRequired();
        builder.Property(x => x.BookName).IsRequired().HasMaxLength(64);
        builder.Property(x => x.InServiceOn).IsRequired();
        builder.Property(x => x.UsefulLifeMonths).IsRequired();
        builder.HasMoney(x => x.ResidualValue, "residual_value");
        builder.HasIndex(x => new { x.TenantId, x.AssetId, x.BookName }).IsUnique()
            .HasDatabaseName("ux_asset_books_tenant_asset_name").HasFilter("deleted_at IS NULL");
    }
}

internal sealed class MaintenanceOrderConfiguration : EntityConfiguration<MaintenanceOrder>
{
    protected override string Schema => Schemas.Assets;
    protected override string TableName => "maintenance_orders";
    protected override void ConfigureEntity(EntityTypeBuilder<MaintenanceOrder> builder)
    {
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.AssetId).IsRequired();
        builder.Property(x => x.Description).IsRequired().HasMaxLength(512);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(x => new { x.TenantId, x.CompanyId, x.AssetId, x.Status })
            .HasDatabaseName("ix_maintenance_orders_tenant_company_asset_status");
    }
}
