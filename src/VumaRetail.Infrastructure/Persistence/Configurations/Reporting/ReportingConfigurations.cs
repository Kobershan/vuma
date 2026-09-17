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

internal sealed class DashboardMeasureConfiguration : EntityConfiguration<DashboardMeasure>
{
    protected override string Schema => Schemas.Reporting; protected override string TableName => "dashboard_measures";
    protected override void ConfigureEntity(EntityTypeBuilder<DashboardMeasure> b)
    { b.Property(x => x.CompanyId).IsRequired(); b.Property(x => x.BusinessDate).IsRequired(); b.Property(x => x.Name).IsRequired().HasMaxLength(128); b.Property(x => x.Currency).IsRequired().HasMaxLength(3); b.Property(x => x.Value).HasPrecision(20, 4).IsRequired(); b.Property(x => x.AsAtUtc).IsRequired(); b.HasIndex(x => new { x.TenantId, x.CompanyId, x.BusinessDate, x.Name, x.Currency }).IsUnique().HasFilter("deleted_at IS NULL"); }
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
        b.Property(x => x.ArtifactReference).HasMaxLength(512);
        b.HasIndex(x => new { x.TenantId, x.OperationId }).IsUnique().HasFilter("deleted_at IS NULL");
        b.HasIndex(x => new { x.TenantId, x.CompanyId, x.ReportCode, x.RequestedAtUtc }).HasFilter("deleted_at IS NULL");
    }
}

internal sealed class ScheduledReportConfiguration : EntityConfiguration<ScheduledReport>
{
    protected override string Schema => Schemas.Reporting; protected override string TableName => "scheduled_reports";
    protected override void ConfigureEntity(EntityTypeBuilder<ScheduledReport> b)
    { b.Property(x => x.CompanyId).IsRequired(); b.Property(x => x.ReportCode).IsRequired().HasMaxLength(64); b.Property(x => x.IntervalMinutes).IsRequired(); b.Property(x => x.NextRunAtUtc).IsRequired(); b.Property(x => x.IsEnabled).IsRequired(); b.HasIndex(x => new { x.TenantId, x.CompanyId, x.NextRunAtUtc }).HasFilter("deleted_at IS NULL"); }
}
