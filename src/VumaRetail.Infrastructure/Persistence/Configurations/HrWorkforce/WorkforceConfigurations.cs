using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.HrWorkforce;

namespace VumaRetail.Infrastructure.Persistence.Configurations.HrWorkforce;

internal sealed class ShiftConfiguration : EntityConfiguration<Shift>
{
    protected override string Schema => Schemas.HrWorkforce; protected override string TableName => "shifts";
    protected override void ConfigureEntity(EntityTypeBuilder<Shift> b) { b.Property(x => x.EmployeeId).IsRequired(); b.Property(x => x.Role).HasMaxLength(128).IsRequired(); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.StartsAt }); }
}
internal sealed class AttendanceRecordConfiguration : EntityConfiguration<AttendanceRecord>
{
    protected override string Schema => Schemas.HrWorkforce; protected override string TableName => "attendance_records";
    protected override void ConfigureEntity(EntityTypeBuilder<AttendanceRecord> b) { b.Property(x => x.EmployeeId).IsRequired(); b.Property(x => x.EventType).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.OccurredAt).IsRequired(); b.Property(x => x.Source).HasMaxLength(64); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.OccurredAt }); }
}
internal sealed class ShiftSwapRequestConfiguration : EntityConfiguration<ShiftSwapRequest>
{
    protected override string Schema => Schemas.HrWorkforce; protected override string TableName => "shift_swap_requests";
    protected override void ConfigureEntity(EntityTypeBuilder<ShiftSwapRequest> b)
    { b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); b.HasIndex(x => new { x.TenantId, x.ShiftId, x.Status }); b.HasIndex(x => new { x.TenantId, x.FromEmployeeId, x.RequestedAt }); }
}
internal sealed class RosterPublicationConfiguration : EntityConfiguration<RosterPublication>
{
    protected override string Schema => Schemas.HrWorkforce; protected override string TableName => "roster_publications";
    protected override void ConfigureEntity(EntityTypeBuilder<RosterPublication> b)
    { b.Property(x => x.CompanyId).IsRequired(); b.Property(x => x.SnapshotHash).HasMaxLength(64).IsRequired(); b.Property(x => x.ShiftCount).IsRequired(); b.Property(x => x.PublishedAt).IsRequired(); b.HasIndex(x => new { x.TenantId, x.CompanyId, x.From, x.To }); }
}
