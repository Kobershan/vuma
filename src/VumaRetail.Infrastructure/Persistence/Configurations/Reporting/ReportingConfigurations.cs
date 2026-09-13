using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Reporting;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Reporting;

internal sealed class ReportDefinitionConfiguration : EntityConfiguration<ReportDefinition>
{
    protected override string Schema => Schemas.Reporting; protected override string TableName => "report_definitions";
    protected override void ConfigureEntity(EntityTypeBuilder<ReportDefinition> b)
    { b.Property(x => x.Code).IsRequired().HasMaxLength(64); b.Property(x => x.Name).IsRequired().HasMaxLength(256); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired(); b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("deleted_at IS NULL"); }
}

internal sealed class ProjectionCheckpointConfiguration : EntityConfiguration<ProjectionCheckpoint>
{
    protected override string Schema => Schemas.Reporting; protected override string TableName => "projection_checkpoints";
    protected override void ConfigureEntity(EntityTypeBuilder<ProjectionCheckpoint> b)
    { b.Property(x => x.CompanyId).IsRequired(); b.Property(x => x.Source).IsRequired().HasMaxLength(128); b.Property(x => x.Generation).IsRequired(); b.Property(x => x.Cursor).IsRequired().HasMaxLength(256); b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Source }).IsUnique().HasFilter("deleted_at IS NULL"); }
}

internal sealed class ReportExportConfiguration : EntityConfiguration<ReportExport>
{
    protected override string Schema => Schemas.Reporting; protected override string TableName => "report_exports";
    protected override void ConfigureEntity(EntityTypeBuilder<ReportExport> b)
    {
        b.Property(x => x.CompanyId).IsRequired();
        b.Property(x => x.OperationId).IsRequired();
        b.Property(x => x.ReportCode).IsRequired().HasMaxLength(64);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.FailureReason).HasMaxLength(512);
        b.HasIndex(x => new { x.TenantId, x.OperationId }).IsUnique().HasFilter("deleted_at IS NULL");
        b.HasIndex(x => new { x.TenantId, x.CompanyId, x.ReportCode, x.RequestedAtUtc }).HasFilter("deleted_at IS NULL");
    }
}
