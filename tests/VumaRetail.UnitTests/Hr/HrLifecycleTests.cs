using VumaRetail.Domain.HrManagement;
using VumaRetail.Domain.HrWorkforce;

namespace VumaRetail.UnitTests.Hr;

public sealed class HrLifecycleTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EmployeeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Employee_normalises_number_and_supports_termination()
    {
        var hired = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var employee = Employee.Create(TenantId, " e-001 ", "Ada", "Lovelace", hired, EmploymentType.Permanent);

        employee.EmployeeNumber.Should().Be("E-001");
        employee.Terminate(hired.AddDays(10));
        employee.Status.Should().Be(EmploymentStatus.Terminated);
    }

    [Fact]
    public void Leave_can_be_decided_once()
    {
        var leave = LeaveRequest.Create(TenantId, EmployeeId, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 3), "Annual");
        leave.Approve(new DateTimeOffset(2026, 1, 15, 8, 0, 0, TimeSpan.Zero));

        leave.Status.Should().Be(LeaveRequestStatus.Approved);
        var act = () => leave.Reject(DateTimeOffset.UtcNow);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Shift_rejects_invalid_window_and_completed_shift_cannot_be_cancelled()
    {
        var start = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var invalid = () => Shift.Create(TenantId, EmployeeId, start, start, "Cashier");
        invalid.Should().Throw<ArgumentException>();

        var shift = Shift.Create(TenantId, EmployeeId, start, start.AddHours(8), "Cashier");
        shift.Complete();
        var cancel = () => shift.Cancel();
        cancel.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Attendance_is_append_only_and_requires_event_type()
    {
        var at = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var attendance = AttendanceRecord.Record(TenantId, EmployeeId, null, AttendanceEventType.ClockIn, at, "mobile");
        attendance.EventType.Should().Be(AttendanceEventType.ClockIn);
        attendance.Source.Should().Be("mobile");

        var invalid = () => AttendanceRecord.Record(TenantId, EmployeeId, null, AttendanceEventType.Unknown, at);
        invalid.Should().Throw<ArgumentException>();
    }
}
