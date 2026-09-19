using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Hr;
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
        var company = Substitute.For<ICompanyContext>();
        var companyId = Guid.NewGuid();
        company.CompanyId.Returns(companyId);
        shift.AssignCompany(companyId);
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

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var result = await new GetEmployeeAvailabilityQueryHandler(employees, shifts, tenant)
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

    [Fact]
    public async Task Shift_swap_handler_requires_shift_owner_and_creates_request()
    {
        var start = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var shift = Shift.Create(TenantId, EmployeeId, start, start.AddHours(8), "Cashier");
        var company = Substitute.For<ICompanyContext>();
        var companyId = Guid.NewGuid();
        company.CompanyId.Returns(companyId);
        shift.AssignCompany(companyId);
        var target = Employee.Create(TenantId, "E-004", "Alan", "Turing", start, EmploymentType.Permanent);
        var employees = Substitute.For<IEmployeeRepository>();
        var shifts = Substitute.For<IShiftRepository>();
        var swaps = Substitute.For<IShiftSwapRequestRepository>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        shifts.FindAsync(shift.Id, Arg.Any<CancellationToken>()).Returns(shift);
        employees.FindAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);

        var id = await new RequestShiftSwapCommandHandler(employees, shifts, swaps, tenant, company)
            .HandleAsync(new RequestShiftSwapCommand(shift.Id, EmployeeId, target.Id, start));

        id.Should().NotBeEmpty();
        swaps.Received(1).Add(Arg.Is<ShiftSwapRequest>(request => request.ShiftId == shift.Id && request.ToEmployeeId == target.Id));
    }

    [Fact]
    public async Task Shift_swap_decision_handler_applies_the_requested_decision()
    {
        var request = ShiftSwapRequest.Request(TenantId, Guid.NewGuid(), EmployeeId, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var company = Substitute.For<ICompanyContext>();
        var companyId = Guid.NewGuid();
        company.CompanyId.Returns(companyId);
        request.AssignCompany(companyId);
        var swaps = Substitute.For<IShiftSwapRequestRepository>();
        var shifts = Substitute.For<IShiftRepository>();
        swaps.FindAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);

        var shift = Shift.Create(TenantId, request.FromEmployeeId, DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddHours(2), "Cashier");
        shift.AssignCompany(companyId);
        shifts.FindAsync(request.ShiftId, Arg.Any<CancellationToken>()).Returns(shift);
        shifts.ListAsync(shift.StartsAt, shift.EndsAt, request.ToEmployeeId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Shift>());

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        await new DecideShiftSwapCommandHandler(swaps, shifts, tenant, company).HandleAsync(new DecideShiftSwapCommand(request.Id, true));

        request.Status.Should().Be(ShiftSwapStatus.Approved);
        shift.EmployeeId.Should().Be(request.ToEmployeeId);
    }

    [Fact]
    public async Task Roster_publication_captures_a_deterministic_hash_and_scope()
    {
        var start = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var shift = Shift.Create(TenantId, EmployeeId, start, start.AddHours(8), "Cashier");
        var shifts = Substitute.For<IShiftRepository>();
        shifts.ListAsync(start, start.AddDays(1), null, Arg.Any<CancellationToken>()).Returns(new[] { shift });
        var publications = Substitute.For<IRosterPublicationRepository>();
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        shift.AssignCompany(company.CompanyId!.Value);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(start);

        var id = await new PublishRosterCommandHandler(shifts, publications, tenant, company, clock)
            .HandleAsync(new PublishRosterCommand(company.CompanyId!.Value, start, start.AddDays(1)));

        id.Should().NotBeEmpty();
        publications.Received(1).Add(Arg.Is<RosterPublication>(publication => publication.ShiftCount == 1 && publication.SnapshotHash.Length == 64));
    }

    [Fact]
    public async Task Roster_publication_counts_only_the_selected_store()
    {
        var start = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        Guid selectedStore = Guid.NewGuid();
        Shift selected = Shift.Create(TenantId, EmployeeId, start, start.AddHours(8), "Cashier", selectedStore);
        Shift other = Shift.Create(TenantId, Guid.NewGuid(), start, start.AddHours(8), "Picker", selectedStore);
        var shifts = Substitute.For<IShiftRepository>();
        shifts.ListAsync(start, start.AddDays(1), null, Arg.Any<CancellationToken>()).Returns(new[] { selected, other });
        var publications = Substitute.For<IRosterPublicationRepository>();
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        selected.AssignCompany(company.CompanyId!.Value);
        other.AssignCompany(Guid.NewGuid());
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(start);

        await new PublishRosterCommandHandler(shifts, publications, tenant, company, clock)
            .HandleAsync(new PublishRosterCommand(company.CompanyId!.Value, start, start.AddDays(1), selectedStore));

        publications.Received(1).Add(Arg.Is<RosterPublication>(publication => publication.ShiftCount == 1));
    }

    [Fact]
    public async Task Shift_swap_request_rejects_a_shift_from_another_active_company()
    {
        var start = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var shift = Shift.Create(TenantId, EmployeeId, start, start.AddHours(8), "Cashier");
        shift.AssignCompany(Guid.NewGuid());
        var employees = Substitute.For<IEmployeeRepository>();
        var shifts = Substitute.For<IShiftRepository>();
        var swaps = Substitute.For<IShiftSwapRequestRepository>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(Guid.NewGuid());
        shifts.FindAsync(shift.Id, Arg.Any<CancellationToken>()).Returns(shift);

        await FluentActions.Invoking(() => new RequestShiftSwapCommandHandler(employees, shifts, swaps, tenant, company)
            .HandleAsync(new RequestShiftSwapCommand(shift.Id, EmployeeId, Guid.NewGuid(), start)))
            .Should().ThrowAsync<InvalidOperationException>();
        swaps.DidNotReceive().Add(Arg.Any<ShiftSwapRequest>());
    }
}
