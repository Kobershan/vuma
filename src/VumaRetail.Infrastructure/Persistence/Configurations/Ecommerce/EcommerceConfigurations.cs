#pragma warning disable CS1591
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Ecommerce;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Ecommerce;

internal sealed class ChannelConnectionConfiguration : EntityConfiguration<ChannelConnection>
{
    protected override string Schema => Schemas.Ecommerce;
    protected override string TableName => "channel_connections";

    protected override void ConfigureEntity(EntityTypeBuilder<ChannelConnection> builder)
    {
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.Code).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Host).IsRequired().HasMaxLength(255);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("deleted_at IS NULL");
        builder.HasIndex(x => new { x.TenantId, x.Host }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

internal sealed class PublishedProductConfiguration : EntityConfiguration<PublishedProduct>
{
    protected override string Schema => Schemas.Ecommerce;
    protected override string TableName => "published_products";

    protected override void ConfigureEntity(EntityTypeBuilder<PublishedProduct> builder)
    {
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.ChannelConnectionId).IsRequired();
        builder.Property(x => x.ItemId);
        builder.Property(x => x.ItemVariantId);
        builder.Property(x => x.Sku).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Price).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        builder.Property(x => x.Available).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.AvailabilityAsAt).IsRequired();
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.PublishedAt).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.ChannelConnectionId, x.Sku, x.Version })
            .IsUnique().HasFilter("deleted_at IS NULL");
        builder.HasIndex(x => new { x.TenantId, x.ChannelConnectionId, x.PublishedAt })
            .HasFilter("deleted_at IS NULL");
    }
}

internal sealed class CommerceBasketConfiguration : EntityConfiguration<CommerceBasket>
{
    protected override string Schema => Schemas.Ecommerce;
    protected override string TableName => "baskets";

    protected override void ConfigureEntity(EntityTypeBuilder<CommerceBasket> builder)
    {
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.ChannelConnectionId).IsRequired();
        builder.Property(x => x.OwnerKey).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(24);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.ChannelConnectionId, x.OwnerKey, x.Status })
            .HasFilter("deleted_at IS NULL");
    }
}
