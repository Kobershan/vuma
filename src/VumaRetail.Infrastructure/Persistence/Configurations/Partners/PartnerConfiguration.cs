using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Partners;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Partners;

/// <summary><c>partners.partners</c>.</summary>
internal sealed class PartnerConfiguration : EntityConfiguration<Partner>
{
    protected override string Schema => Schemas.Partners;

    protected override string TableName => "partners";

    protected override void ConfigureEntity(EntityTypeBuilder<Partner> builder)
    {
        builder.Property(partner => partner.Code).IsRequired().HasMaxLength(32);
        builder.Property(partner => partner.Name).IsRequired().HasMaxLength(256);

        // [Flags] PartnerType stored as text (docs/DATA_MODEL.md §2), for example "Customer, Supplier" —
        // an ordinal would turn a reordered flag into silently relabelled history.
        builder.Property(partner => partner.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.HasAddress(partner => partner.Address, "address", isRequired: false);

        builder.Property(partner => partner.Email).HasMaxLength(256);
        builder.Property(partner => partner.Phone).HasMaxLength(32);
        builder.Property(partner => partner.TaxNumber).HasMaxLength(32);
        builder.Property(partner => partner.IsActive).IsRequired();

        builder.HasIndex(partner => new { partner.TenantId, partner.Code })
            .IsUnique()
            .HasDatabaseName("ux_partners_tenant_id_code")
            .HasFilter("deleted_at IS NULL");
    }
}
