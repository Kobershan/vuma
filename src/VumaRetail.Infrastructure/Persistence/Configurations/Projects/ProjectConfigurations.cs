using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Projects;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Projects;

internal sealed class ProjectConfiguration : EntityConfiguration<Project>
{
    protected override string Schema => Schemas.Projects;
    protected override string TableName => "projects";
    protected override void ConfigureEntity(EntityTypeBuilder<Project> b)
    {
        b.Property(x => x.CompanyId).IsRequired(); b.Property(x => x.Code).IsRequired().HasMaxLength(64);
        b.Property(x => x.Name).IsRequired().HasMaxLength(256); b.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

internal sealed class ProjectBudgetConfiguration : EntityConfiguration<ProjectBudget>
{
    protected override string Schema => Schemas.Projects; protected override string TableName => "project_budgets";
    protected override void ConfigureEntity(EntityTypeBuilder<ProjectBudget> b)
    {
        b.Property(x => x.CompanyId).IsRequired(); b.Property(x => x.ProjectId).IsRequired(); b.Property(x => x.Version).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired(); b.HasMoney(x => x.Amount, "amount");
        b.HasMoney(x => x.Committed, "committed"); b.HasMoney(x => x.Actual, "actual");
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Version }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

internal sealed class ProjectContractConfiguration : EntityConfiguration<ProjectContract>
{
    protected override string Schema => Schemas.Projects; protected override string TableName => "project_contracts";
    protected override void ConfigureEntity(EntityTypeBuilder<ProjectContract> b)
    { b.Property(x => x.CompanyId).IsRequired(); b.Property(x => x.ProjectId).IsRequired(); b.Property(x => x.Number).IsRequired().HasMaxLength(64); b.HasMoney(x => x.OriginalValue, "original_value"); b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Number }).IsUnique().HasFilter("deleted_at IS NULL"); }
}

internal sealed class ContractVariationConfiguration : EntityConfiguration<ContractVariation>
{
    protected override string Schema => Schemas.Projects; protected override string TableName => "contract_variations";
    protected override void ConfigureEntity(EntityTypeBuilder<ContractVariation> b)
    { b.Property(x => x.CompanyId).IsRequired(); b.Property(x => x.ContractId).IsRequired(); b.Property(x => x.Reason).IsRequired().HasMaxLength(512); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired(); b.HasMoney(x => x.Amount, "amount"); b.HasIndex(x => new { x.TenantId, x.ContractId, x.Status }).HasFilter("deleted_at IS NULL"); }
}

internal sealed class BillingMilestoneConfiguration : EntityConfiguration<BillingMilestone>
{
    protected override string Schema => Schemas.Projects; protected override string TableName => "billing_milestones";
    protected override void ConfigureEntity(EntityTypeBuilder<BillingMilestone> b)
    { b.Property(x => x.CompanyId).IsRequired(); b.Property(x => x.ContractId).IsRequired(); b.Property(x => x.Name).IsRequired().HasMaxLength(128); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired(); b.HasMoney(x => x.Amount, "amount"); b.HasIndex(x => new { x.TenantId, x.ContractId, x.Name }).IsUnique().HasFilter("deleted_at IS NULL"); }
}
