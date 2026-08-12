using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Platform;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Platform;

/// <summary><c>platform.tenants</c>.</summary>
internal sealed class TenantConfiguration : EntityConfiguration<Tenant>
{
    protected override string Schema => Schemas.Platform;

    protected override string TableName => "tenants";

    protected override void ConfigureEntity(EntityTypeBuilder<Tenant> builder)
    {
        builder.Property(tenant => tenant.LegalName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(tenant => tenant.TradingName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(tenant => tenant.Locale)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(tenant => tenant.BaseCurrency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();

        // IANA identifiers, not offsets — "Africa/Johannesburg", not "+02:00". Offsets move with
        // legislation; the identifier survives it.
        builder.Property(tenant => tenant.TimeZone)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(tenant => tenant.TaxRegistrationNumber)
            .HasMaxLength(64);

        builder.Property(tenant => tenant.IsActive)
            .IsRequired();
    }
}
