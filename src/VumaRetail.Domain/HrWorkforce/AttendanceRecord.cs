 #pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.HrWorkforce;

/// <summary>Immutable attendance event.</summary>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.AppendOnly)]
public sealed class AttendanceRecord : Entity, IImmutableRecord
{
    private AttendanceRecord(Guid tenantId, Guid employeeId, Guid? shiftId, AttendanceEventType type, DateTimeOffset at, string? source) : base(tenantId) { EmployeeId = employeeId; ShiftId = shiftId; EventType = type; OccurredAt = at; Source = source; }
    private AttendanceRecord() { }
    public Guid EmployeeId { get; private set; }
    public Guid? ShiftId { get; private set; }
    public AttendanceEventType EventType { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Source { get; private set; }
    public static AttendanceRecord Record(Guid tenantId, Guid employeeId, Guid? shiftId, AttendanceEventType type, DateTimeOffset at, string? source = null)
    { if (tenantId == Guid.Empty || employeeId == Guid.Empty) throw new ArgumentException("Tenant and employee are required."); if (type == AttendanceEventType.Unknown) throw new ArgumentException("Event type is required.", nameof(type)); return new AttendanceRecord(tenantId, employeeId, shiftId, type, at, string.IsNullOrWhiteSpace(source) ? null : source.Trim()); }
}
/// <summary>Attendance event type.</summary>
public enum AttendanceEventType
{
    /// <summary>Unset.</summary>
    Unknown = 0,
    /// <summary>Clock-in.</summary>
    ClockIn = 1,
    /// <summary>Clock-out.</summary>
    ClockOut = 2,
    /// <summary>Break begins.</summary>
    BreakStart = 3,
    /// <summary>Break ends.</summary>
    BreakEnd = 4,
}
