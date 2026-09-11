 #pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.HrManagement;

/// <summary>Employee leave request.</summary>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.LastWriterWins)]
public sealed class LeaveRequest : Entity
{
    private LeaveRequest(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, string type, string? reason) : base(tenantId) { EmployeeId = employeeId; From = from; To = to; LeaveType = type; Reason = reason; }
    private LeaveRequest() { }
    public Guid EmployeeId { get; private set; }
    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public string LeaveType { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public LeaveRequestStatus Status { get; private set; } = LeaveRequestStatus.Pending;
    public DateTimeOffset? DecidedAt { get; private set; }
    public static LeaveRequest Create(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, string type, string? reason = null)
    { if (tenantId == Guid.Empty || employeeId == Guid.Empty) throw new ArgumentException("Tenant and employee are required."); if (to < from) throw new ArgumentException("End cannot precede start.", nameof(to)); if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException("Leave type is required.", nameof(type)); return new LeaveRequest(tenantId, employeeId, from, to, type.Trim(), string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()); }
    public void Approve(DateTimeOffset at) { EnsurePending(); Status = LeaveRequestStatus.Approved; DecidedAt = at; }
    public void Reject(DateTimeOffset at) { EnsurePending(); Status = LeaveRequestStatus.Rejected; DecidedAt = at; }
    private void EnsurePending() { if (Status != LeaveRequestStatus.Pending) throw new InvalidOperationException("Only pending leave can be decided."); }
}
/// <summary>Leave decision state.</summary>
public enum LeaveRequestStatus
{
    /// <summary>Awaiting decision.</summary>
    Pending = 1,
    /// <summary>Approved.</summary>
    Approved = 2,
    /// <summary>Rejected.</summary>
    Rejected = 3,
}
