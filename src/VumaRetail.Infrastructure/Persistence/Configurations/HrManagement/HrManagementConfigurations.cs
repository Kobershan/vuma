using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.HrManagement;

namespace VumaRetail.Infrastructure.Persistence.Configurations.HrManagement;

internal sealed class EmploymentContractConfiguration : EntityConfiguration<EmploymentContract>
{
    protected override string Schema => Schemas.HrManagement; protected override string TableName => "employment_contracts";
    protected override void ConfigureEntity(EntityTypeBuilder<EmploymentContract> b) { b.Property(x => x.EmployeeId).IsRequired(); b.Property(x => x.HourlyRate).HasPrecision(19, 4).IsRequired(); b.Property(x => x.Currency).HasMaxLength(3).IsRequired(); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.StartsOn }).IsUnique().HasDatabaseName("ux_employment_contracts_tenant_employee_start").HasFilter("deleted_at IS NULL"); }
}
internal sealed class LeaveRequestConfiguration : EntityConfiguration<LeaveRequest>
{
    protected override string Schema => Schemas.HrManagement; protected override string TableName => "leave_requests";
    protected override void ConfigureEntity(EntityTypeBuilder<LeaveRequest> b) { b.Property(x => x.EmployeeId).IsRequired(); b.Property(x => x.LeaveType).HasMaxLength(64).IsRequired(); b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.From }); }
}
internal sealed class EmployeeConfiguration : EntityConfiguration<Employee>
{
    protected override string Schema => Schemas.HrManagement; protected override string TableName => "employees";
    protected override void ConfigureEntity(EntityTypeBuilder<Employee> b) { b.Property(x => x.EmployeeNumber).IsRequired().HasMaxLength(32); b.Property(x => x.FirstName).IsRequired().HasMaxLength(128); b.Property(x => x.LastName).IsRequired().HasMaxLength(128); b.Property(x => x.PreferredName).HasMaxLength(128); b.Property(x => x.Email).HasMaxLength(256); b.Property(x => x.Phone).HasMaxLength(32); b.Property(x => x.EmploymentType).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); b.HasIndex(x => new { x.TenantId, x.EmployeeNumber }).IsUnique().HasDatabaseName("ux_employees_tenant_id_employee_number").HasFilter("deleted_at IS NULL"); }
}
