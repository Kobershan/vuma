using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Registry;

/// <summary><c>registry.company_groups</c>.</summary>
internal sealed class CompanyGroupConfiguration : EntityConfiguration<CompanyGroup>
{
    protected override string Schema => Schemas.Registry;

    protected override string TableName => "company_groups";

    /// <inheritdoc />
    protected override bool MapsCompanyId => false;

    protected override void ConfigureEntity(EntityTypeBuilder<CompanyGroup> builder)
    {
        builder.Property(group => group.Name)
            .IsRequired()
            .HasMaxLength(256);
    }
}
