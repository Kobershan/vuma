using VumaRetail.Domain.HrManagement;
using VumaRetail.Domain.HrWorkforce;
using VumaRetail.Application.Hr;
using NSubstitute;

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
    public void Employee_can_be_suspended_and_reactivated_before_termination()
    {
        var hired = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var employee = Employee.Create(TenantId, "E-002", "Grace", "Hopper", hired, EmploymentType.Permanent);

        employee.Suspend();
        employee.Status.Should().Be(EmploymentStatus.Suspended);

        employee.Activate();
        employee.Status.Should().Be(EmploymentStatus.Active);

        var terminatedAt = hired.AddDays(30);
        employee.Terminate(terminatedAt);
        employee.Status.Should().Be(EmploymentStatus.Terminated);
        employee.TerminatedAt.Should().Be(terminatedAt);
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
    public void Shift_overlap_is_detected_only_when_windows_intersect()
    {
        var start = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var shift = Shift.Create(TenantId, EmployeeId, start, start.AddHours(8), "Cashier");

        shift.Overlaps(start.AddHours(7), start.AddHours(9)).Should().BeTrue();
        shift.Overlaps(start.AddHours(8), start.AddHours(10)).Should().BeFalse();
    }

    [Fact]
    public async Task Availability_excludes_cancelled_shifts_and_inactive_employees()
    {
        var hired = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var employee = Employee.Create(TenantId, "E-003", "Katherine", "Johnson", hired, EmploymentType.Permanent);
        var from = hired.AddDays(1);
        var shift = Shift.Create(TenantId, employee.Id, from, from.AddHours(8), "Analyst");
        var employees = Substitute.For<IEmployeeRepository>();
        var shifts = Substitute.For<IShiftRepository>();
        employees.FindAsync(employee.Id, Arg.Any<CancellationToken>()).Returns(employee);
        shifts.ListAsync(from, from.AddHours(2), employee.Id, Arg.Any<CancellationToken>()).Returns(new[] { shift });

        var result = await new GetEmployeeAvailabilityQueryHandler(employees, shifts)
            .HandleAsync(new GetEmployeeAvailabilityQuery(employee.Id, from, from.AddHours(2)));

        result.Available.Should().BeFalse();
        result.ScheduledShifts.Should().ContainSingle();
    }

    [Fact]
    public void Shift_swap_requires_two_employees_and_is_decided_once()
    {
        var at = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var action = () => ShiftSwapRequest.Request(TenantId, Guid.NewGuid(), EmployeeId, EmployeeId, at);
        action.Should().Throw<ArgumentException>();

        var swap = ShiftSwapRequest.Request(TenantId, Guid.NewGuid(), EmployeeId, Guid.NewGuid(), at);
        swap.Approve();
        swap.Status.Should().Be(ShiftSwapStatus.Approved);
        var reject = () => swap.Reject();
        reject.Should().Throw<InvalidOperationException>();
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
