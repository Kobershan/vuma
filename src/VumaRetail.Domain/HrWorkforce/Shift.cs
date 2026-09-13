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
    public bool Overlaps(DateTimeOffset start, DateTimeOffset end)
        => StartsAt < end && EndsAt > start;
    public void Cancel() { if (Status == ShiftStatus.Completed) throw new InvalidOperationException("Completed shifts cannot be cancelled."); Status = ShiftStatus.Cancelled; }
    public void Complete() { if (Status == ShiftStatus.Cancelled) throw new InvalidOperationException("Cancelled shifts cannot be completed."); Status = ShiftStatus.Completed; }
    public void TransferTo(Guid employeeId)
    { if (employeeId == Guid.Empty) throw new ArgumentException("Employee is required.", nameof(employeeId)); if (Status != ShiftStatus.Planned) throw new InvalidOperationException("Only planned shifts can be transferred."); EmployeeId = employeeId; }
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

/// <summary>Approval state for a requested shift transfer.</summary>
public enum ShiftSwapStatus { Pending, Approved, Rejected }

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ShiftSwapRequest : Entity
{
    private ShiftSwapRequest(Guid tenantId, Guid shiftId, Guid fromEmployeeId, Guid toEmployeeId,
        DateTimeOffset requestedAt) : base(tenantId)
    {
        ShiftId = shiftId;
        FromEmployeeId = fromEmployeeId;
        ToEmployeeId = toEmployeeId;
        RequestedAt = requestedAt.ToUniversalTime();
    }

    private ShiftSwapRequest() { }
    public Guid ShiftId { get; private set; }
    public Guid FromEmployeeId { get; private set; }
    public Guid ToEmployeeId { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public ShiftSwapStatus Status { get; private set; } = ShiftSwapStatus.Pending;

    public static ShiftSwapRequest Request(Guid tenantId, Guid shiftId, Guid fromEmployeeId, Guid toEmployeeId,
        DateTimeOffset requestedAt)
    {
        if (tenantId == Guid.Empty || shiftId == Guid.Empty || fromEmployeeId == Guid.Empty || toEmployeeId == Guid.Empty)
            throw new ArgumentException("Shift swap identities are required.");
        if (fromEmployeeId == toEmployeeId)
            throw new ArgumentException("A shift cannot be swapped with the same employee.", nameof(toEmployeeId));
        return new ShiftSwapRequest(tenantId, shiftId, fromEmployeeId, toEmployeeId, requestedAt);
    }

    public void Approve()
    {
        EnsurePending();
        Status = ShiftSwapStatus.Approved;
    }

    public void Reject()
    {
        EnsurePending();
        Status = ShiftSwapStatus.Rejected;
    }

    private void EnsurePending()
    {
        if (Status != ShiftSwapStatus.Pending)
            throw new InvalidOperationException("Only a pending shift swap can be decided.");
    }
}
