#pragma warning disable CS1591
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Marketing;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Marketing;

internal sealed class MarketingCampaignConfiguration : EntityConfiguration<MarketingCampaign>
{
    protected override string Schema => Schemas.Marketing;
    protected override string TableName => "campaigns";
    protected override void ConfigureEntity(EntityTypeBuilder<MarketingCampaign> b)
    {
        b.Property(x => x.CompanyId).IsRequired();
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.TemplateId).HasMaxLength(128).IsRequired();
        b.Property(x => x.ScheduledAt).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Status, x.ScheduledAt });
    }
}

internal sealed class OutboundMessageConfiguration : EntityConfiguration<OutboundMessage>
{
    protected override string Schema => Schemas.Marketing;
    protected override string TableName => "outbound_messages";
    protected override void ConfigureEntity(EntityTypeBuilder<OutboundMessage> b)
    {
        b.Property(x => x.CompanyId).IsRequired();
        b.Property(x => x.IdempotencyKey).HasMaxLength(256).IsRequired();
        b.Property(x => x.ScheduledAt).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.CompanyId, x.IdempotencyKey }).IsUnique()
            .HasDatabaseName("ux_outbound_messages_tenant_company_idempotency")
            .HasFilter("deleted_at IS NULL");
        b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Status, x.ScheduledAt });
    }
}
