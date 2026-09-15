using Microsoft.EntityFrameworkCore;

namespace VumaRetail.ControlPlane;

public sealed class ControlPlaneDbContext(DbContextOptions<ControlPlaneDbContext> options) : DbContext(options)
{
    public DbSet<ControlPlaneDevice> Devices => Set<ControlPlaneDevice>();
    public DbSet<ControlPlaneRequest> Requests => Set<ControlPlaneRequest>();
    public DbSet<ControlPlaneMeteringReceipt> MeteringReceipts => Set<ControlPlaneMeteringReceipt>();
    public DbSet<ControlPlaneAuditEntry> AuditEntries => Set<ControlPlaneAuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ControlPlaneDevice>().HasKey(x => x.NodeId);
        builder.Entity<ControlPlaneDevice>().HasIndex(x => x.InstallId).IsUnique();
        builder.Entity<ControlPlaneRequest>().HasKey(x => x.RequestId);
        builder.Entity<ControlPlaneMeteringReceipt>().HasKey(x => x.RequestId);
        builder.Entity<ControlPlaneMeteringReceipt>().HasIndex(x => new { x.NodeId, x.Period }).IsUnique();
        builder.Entity<ControlPlaneAuditEntry>().HasKey(x => x.Id);
    }
}

public sealed class ControlPlaneDevice
{
    public string NodeId { get; set; } = string.Empty;
    public string InstallId { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string LeaseId { get; set; } = string.Empty;
    public DateTimeOffset ActivatedAtUtc { get; set; }
}

public sealed class ControlPlaneRequest
{
    public Guid RequestId { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
}

public sealed class ControlPlaneMeteringReceipt
{
    public Guid RequestId { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public DateOnly Period { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAtUtc { get; set; }
}

public sealed class ControlPlaneAuditEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
}
