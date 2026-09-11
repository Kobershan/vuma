 #pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.HrWorkforce;

/// <summary>Scheduled work period for an employee.</summary>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.LastWriterWins)]
public sealed class Shift : Entity
{
    private Shift(Guid tenantId, Guid employeeId, DateTimeOffset start, DateTimeOffset end, string role, Guid? storeId) : base(tenantId, storeId) { EmployeeId = employeeId; StartsAt = start; EndsAt = end; Role = role; }
    private Shift() { }
    public Guid EmployeeId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public string Role { get; private set; } = string.Empty;
    public ShiftStatus Status { get; private set; } = ShiftStatus.Planned;
    public static Shift Create(Guid tenantId, Guid employeeId, DateTimeOffset start, DateTimeOffset end, string role, Guid? storeId = null)
    { if (tenantId == Guid.Empty || employeeId == Guid.Empty) throw new ArgumentException("Tenant and employee are required."); if (end <= start) throw new ArgumentException("Shift must end after it starts.", nameof(end)); if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("Role is required.", nameof(role)); return new Shift(tenantId, employeeId, start, end, role.Trim(), storeId); }
    public void Cancel() { if (Status == ShiftStatus.Completed) throw new InvalidOperationException("Completed shifts cannot be cancelled."); Status = ShiftStatus.Cancelled; }
    public void Complete() { if (Status == ShiftStatus.Cancelled) throw new InvalidOperationException("Cancelled shifts cannot be completed."); Status = ShiftStatus.Completed; }
}
/// <summary>Shift state.</summary>
public enum ShiftStatus
{
    /// <summary>Planned.</summary>
    Planned = 1,
    /// <summary>Cancelled.</summary>
    Cancelled = 2,
    /// <summary>Completed.</summary>
    Completed = 3,
}
