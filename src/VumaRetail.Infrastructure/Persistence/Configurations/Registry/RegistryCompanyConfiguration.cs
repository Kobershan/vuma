using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Registry;

/// <summary><c>registry.companies</c>.</summary>
internal sealed class RegistryCompanyConfiguration : EntityConfiguration<RegistryCompany>
{
    protected override string Schema => Schemas.Registry;

    protected override string TableName => "companies";

    protected override void ConfigureEntity(EntityTypeBuilder<RegistryCompany> builder)
    {
        builder.Property(company => company.Code)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(company => company.LegalName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(company => company.TradingName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(company => company.RegistrationNumber)
            .HasMaxLength(64);

        builder.Property(company => company.TaxRegistrationNumber)
            .HasMaxLength(64);

        builder.Property(company => company.BaseCurrency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(company => company.Locale)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(company => company.DocumentPrefix)
            .IsRequired()
            .HasMaxLength(16);

        // Encrypted at rest and never plaintext at this layer (ADR-118, R10) — the ciphertext is what
        // EF stores; only IConnectionSecretProtector ever sees the clear connection string.
        builder.Property(company => company.ConnectionCiphertext)
            .IsRequired();

        builder.Property(company => company.LifecycleState)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(company => company.IsActive)
            .IsRequired();

        builder.Property(company => company.FailedStep)
            .HasMaxLength(64);

        builder.Property(company => company.FailureReason)
            .HasMaxLength(1024);

        builder.Property(company => company.SchemaVersion)
            .HasMaxLength(64);

        builder.Property(company => company.MigrationState)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        // A company code appears on every document that company issues and in the barcode routing
        // index; two live companies sharing one would make both unusable. Unique among live rows only,
        // so a soft-deleted company does not reserve its code forever (§7 rule 8).
        builder.HasIndex(company => new { company.TenantId, company.Code })
            .IsUnique()
            .HasDatabaseName("ux_companies_tenant_id_code")
            .HasFilter("deleted_at IS NULL");

        // The hot query for "which companies does business logic see" (ADR-118) — the group fan-out
        // and every group read model start here.
        builder.HasIndex(company => new { company.TenantId, company.LifecycleState, company.IsActive })
            .HasDatabaseName("ix_companies_tenant_id_lifecycle_state_is_active");
    }
}
