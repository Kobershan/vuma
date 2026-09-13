using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Quality;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Quality;

internal sealed class InspectionPlanConfiguration : EntityConfiguration<InspectionPlan>
{
    protected override string Schema => Schemas.Quality;
    protected override string TableName => "inspection_plans";
    protected override void ConfigureEntity(EntityTypeBuilder<InspectionPlan> builder)
    {
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.ItemId);
        builder.Property(x => x.ItemVariantId);
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(256);
        builder.Property(x => x.SampleSize).IsRequired();
        builder.Property(x => x.AcceptanceCriteria).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(x => new { x.TenantId, x.CompanyId, x.ItemId, x.ItemVariantId, x.Version })
            .IsUnique().HasDatabaseName("ux_inspection_plans_tenant_company_target_version")
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(x => new { x.TenantId, x.CompanyId, x.Status })
            .HasDatabaseName("ix_inspection_plans_tenant_company_status")
            .HasFilter("deleted_at IS NULL");
    }
}

internal sealed class QualityCertificateConfiguration : EntityConfiguration<QualityCertificate>
{
    protected override string Schema => Schemas.Quality;
    protected override string TableName => "quality_certificates";
    protected override void ConfigureEntity(EntityTypeBuilder<QualityCertificate> builder)
    {
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.CertificateNumber).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Issuer).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Evidence).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.RevocationReason).HasMaxLength(512);
        builder.HasIndex(x => new { x.TenantId, x.CompanyId, x.CertificateNumber })
            .IsUnique().HasDatabaseName("ux_quality_certificates_tenant_company_number")
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(x => new { x.TenantId, x.CompanyId, x.Status })
            .HasDatabaseName("ix_quality_certificates_tenant_company_status")
            .HasFilter("deleted_at IS NULL");
    }
}

internal sealed class RecallCaseConfiguration : EntityConfiguration<RecallCase>
{
    private static readonly JsonSerializerOptions SerializerOptions = new();
    protected override string Schema => Schemas.Quality;
    protected override string TableName => "recall_cases";
    protected override void ConfigureEntity(EntityTypeBuilder<RecallCase> builder)
    {
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.OperationId).IsRequired();
        builder.Property(x => x.CaseNumber).IsRequired().HasMaxLength(128);
        builder.Property(x => x.LotReference).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ClosureReason).HasMaxLength(2000);
        builder.Property(x => x.TraceReferences).HasField("_trace").HasColumnName("trace_references").HasColumnType("jsonb").IsRequired()
            .HasConversion(value => JsonSerializer.Serialize(value, SerializerOptions),
                json => JsonSerializer.Deserialize<List<RecallTraceReference>>(json, SerializerOptions) ?? new List<RecallTraceReference>());
        builder.HasIndex(x => new { x.TenantId, x.OperationId }).IsUnique()
            .HasDatabaseName("ux_recall_cases_tenant_operation").HasFilter("deleted_at IS NULL");
        builder.HasIndex(x => new { x.TenantId, x.CompanyId, x.Status })
            .HasDatabaseName("ix_recall_cases_tenant_company_status").HasFilter("deleted_at IS NULL");
    }
}

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
        builder.Property(hold => hold.BatchReference).HasMaxLength(128);
        builder.Property(hold => hold.ExpiryDate);
        builder.Property(hold => hold.SerialNumber).HasMaxLength(128);
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

internal sealed class InspectionResultConfiguration : EntityConfiguration<InspectionResult>
{
    protected override string Schema => Schemas.Quality;
    protected override string TableName => "inspection_results";
    protected override void ConfigureEntity(EntityTypeBuilder<InspectionResult> builder)
    {
        builder.Property(x => x.HoldId).IsRequired();
        builder.Property(x => x.PlanId);
        builder.Property(x => x.PlanVersion).IsRequired();
        builder.Property(x => x.OperationId).IsRequired();
        builder.Property(x => x.SampleSize).IsRequired();
        builder.Property(x => x.Evidence).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.InspectedAt).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.OperationId }).IsUnique()
            .HasDatabaseName("ux_inspection_results_tenant_operation").HasFilter("deleted_at IS NULL");
        builder.HasIndex(x => new { x.TenantId, x.CompanyId, x.HoldId })
            .HasDatabaseName("ix_inspection_results_tenant_company_hold").HasFilter("deleted_at IS NULL");
    }
}

internal sealed class NonConformanceConfiguration : EntityConfiguration<NonConformance>
{
    protected override string Schema => Schemas.Quality;
    protected override string TableName => "non_conformances";
    protected override void ConfigureEntity(EntityTypeBuilder<NonConformance> builder)
    {
        builder.Property(x => x.HoldId).IsRequired();
        builder.Property(x => x.CorrectiveActionOperationId);
        builder.Property(x => x.ClosureOperationId);
        builder.Property(x => x.OperationId).IsRequired();
        builder.Property(x => x.Severity).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(24);
        builder.Property(x => x.Description).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.Resolution).HasMaxLength(2000);
        builder.HasIndex(x => new { x.TenantId, x.OperationId }).IsUnique()
            .HasDatabaseName("ux_non_conformances_tenant_operation").HasFilter("deleted_at IS NULL");
        builder.HasIndex(x => new { x.TenantId, x.CompanyId, x.Status })
            .HasDatabaseName("ix_non_conformances_tenant_company_status").HasFilter("deleted_at IS NULL");
    }
}
