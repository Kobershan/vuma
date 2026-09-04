using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Registry;

public class CompanyLinkConfiguration : IEntityTypeConfiguration<CompanyLink>
{
    public void Configure(EntityTypeBuilder<CompanyLink> builder)
    {
        builder.ToTable("company_links", "registry");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OperatorId).IsRequired();
        builder.Property(x => x.OperatorName).HasMaxLength(256);
        builder.Property(x => x.CompanyAId).IsRequired();
        builder.Property(x => x.CompanyBId).IsRequired();
        builder.Property(x => x.Scopes).IsRequired();
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.EffectiveFrom).IsRequired();
        builder.Property(x => x.EffectiveTo);
        builder.Property(x => x.AcceptedByA);
        builder.Property(x => x.AcceptedByB);
        builder.Property(x => x.AcceptedAt);
        builder.Property(x => x.SuspendedReason).HasMaxLength(500);
        builder.Property(x => x.SuspendedAt);
        builder.Property(x => x.RevokedReason).HasMaxLength(500);
        builder.Property(x => x.RevokedAt);

        builder.HasIndex(x => new { x.TenantId, x.CompanyAId, x.CompanyBId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.OperatorId });
    }
}

public class OperatorConfiguration : IEntityTypeConfiguration<Operator>
{
    public void Configure(EntityTypeBuilder<Operator> builder)
    {
        builder.ToTable("operators", "registry");
        builder.HasKey(x => x.OperatorId);
        builder.Property(x => x.OperatorId).ValueGeneratedNever();
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.LicenceFingerprint).HasMaxLength(128).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}

public class PremisesConfiguration : IEntityTypeConfiguration<Premises>
{
    public void Configure(EntityTypeBuilder<Premises> builder)
    {
        builder.ToTable("premises", "registry");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Address).HasMaxLength(500).IsRequired();
        builder.Property(x => x.GeoLocation).HasMaxLength(256).IsRequired();
        builder.Property(x => x.TradingHours).HasMaxLength(128);
        builder.Property(x => x.IsActive).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}

public class PremisesOccupancyConfiguration : IEntityTypeConfiguration<PremisesOccupancy>
{
    public void Configure(EntityTypeBuilder<PremisesOccupancy> builder)
    {
        builder.ToTable("premises_occupancies", "registry");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PremisesId).IsRequired();
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.StoreId).IsRequired();
        builder.Property(x => x.OccupiesFrom).IsRequired();
        builder.Property(x => x.OccupiesTo);

        builder.HasIndex(x => new { x.PremisesId, x.CompanyId });
    }
}

public class PremisesBinLayoutConfiguration : IEntityTypeConfiguration<PremisesBinLayout>
{
    public void Configure(EntityTypeBuilder<PremisesBinLayout> builder)
    {
        builder.ToTable("premises_bin_layouts", "registry");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PremisesId).IsRequired();
        builder.Property(x => x.ZoneCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.BinCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.IsShared).IsRequired();

        builder.HasIndex(x => new { x.PremisesId, x.ZoneCode, x.BinCode });
    }
}

public class RegistryUserConfiguration : IEntityTypeConfiguration<RegistryUser>
{
    public void Configure(EntityTypeBuilder<RegistryUser> builder)
    {
        builder.ToTable("registry_users", "registry");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Login).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ContactDetails).HasMaxLength(500);
        builder.Property(x => x.OperatorId).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.IsEnabled).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Login }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.OperatorId });
    }
}

public class RegistryUserCompanyAccessConfiguration : IEntityTypeConfiguration<RegistryUserCompanyAccess>
{
    public void Configure(EntityTypeBuilder<RegistryUserCompanyAccess> builder)
    {
        builder.ToTable("user_company_access", "registry");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RegistryUserId).IsRequired();
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.Roles).HasMaxLength(500).IsRequired();
        builder.Property(x => x.GrantedBy).HasMaxLength(128).IsRequired();
        builder.Property(x => x.GrantedAt).IsRequired();

        builder.HasIndex(x => new { x.RegistryUserId, x.CompanyId });
    }
}

public class RegistryTerminalConfiguration : IEntityTypeConfiguration<RegistryTerminal>
{
    public void Configure(EntityTypeBuilder<RegistryTerminal> builder)
    {
        builder.ToTable("terminals", "registry");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.PremisesId).IsRequired();
        builder.Property(x => x.TerminalId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DeviceCertThumbprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CompanyIds)
            .HasConversion(
                v => v.ToArray(),
                v => v.ToList());

        builder.HasIndex(x => new { x.TenantId, x.TerminalId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.PremisesId });
    }
}
