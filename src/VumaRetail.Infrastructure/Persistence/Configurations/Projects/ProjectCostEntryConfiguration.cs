using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Projects;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Projects;

internal sealed class ProjectCostEntryConfiguration : EntityConfiguration<ProjectCostEntry>
{
    protected override string Schema => Schemas.Projects; protected override string TableName => "project_cost_entries";
    protected override void ConfigureEntity(EntityTypeBuilder<ProjectCostEntry> b)
    { b.Property(x => x.CompanyId).IsRequired(); b.Property(x => x.ProjectId).IsRequired(); b.Property(x => x.SourceReference).IsRequired().HasMaxLength(128); b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16).IsRequired(); b.Property(x => x.ReversesEntryId); b.HasMoney(x => x.Amount, "amount"); b.HasIndex(x => new { x.TenantId, x.ProjectId, x.SourceReference }).IsUnique().HasFilter("deleted_at IS NULL"); b.HasIndex(x => new { x.TenantId, x.ReversesEntryId }).HasFilter("reverses_entry_id IS NOT NULL AND deleted_at IS NULL"); }
}
