using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Hr;
using VumaRetail.Domain.HrManagement;
using VumaRetail.Domain.HrWorkforce;

namespace VumaRetail.UnitTests.Hr;

public sealed class PayrollExportTests
{
    [Fact]
    public async Task Export_calculates_worked_hours_minus_breaks_at_contract_rate()
    {
        var tenantId = Guid.NewGuid();
        var employee = Employee.Create(tenantId, "E-200", "Payroll", "User", DateTimeOffset.UtcNow, EmploymentType.Permanent);
        var start = new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            AttendanceRecord.Record(tenantId, employee.Id, null, AttendanceEventType.ClockIn, start),
            AttendanceRecord.Record(tenantId, employee.Id, null, AttendanceEventType.BreakStart, start.AddHours(2)),
            AttendanceRecord.Record(tenantId, employee.Id, null, AttendanceEventType.BreakEnd, start.AddHours(3)),
            AttendanceRecord.Record(tenantId, employee.Id, null, AttendanceEventType.ClockOut, start.AddHours(8)),
        };
        var employees = Substitute.For<IEmployeeRepository>();
        employees.ListAsync(Arg.Any<CancellationToken>()).Returns(new[] { employee });
        var contracts = Substitute.For<IEmploymentContractRepository>();
        contracts.ListAsync(employee.Id, Arg.Any<CancellationToken>()).Returns(new[]
        {
            EmploymentContract.Create(tenantId, employee.Id, new DateOnly(2026, 1, 1), null, 100m, "zar")
        });
        var attendance = Substitute.For<IAttendanceRepository>();
        attendance.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), null, Arg.Any<CancellationToken>()).Returns(events);

        var result = await new GeneratePayrollExportQueryHandler(employees, contracts, attendance)
            .HandleAsync(new GeneratePayrollExportQuery(new DateOnly(2026, 9, 13), new DateOnly(2026, 9, 13)));

        result.Should().ContainSingle().Which.Should().Match<PayrollExportRow>(row =>
            row.Hours == 7m && row.HourlyRate == 100m && row.GrossAmount == 700m && row.Currency == "ZAR");
    }

    [Fact]
    public async Task Export_rejects_unclosed_attendance_instead_of_underpaying()
    {
        var tenantId = Guid.NewGuid();
        var employee = Employee.Create(tenantId, "E-201", "Open", "Session", DateTimeOffset.UtcNow, EmploymentType.Permanent);
        var attendance = Substitute.For<IAttendanceRepository>();
        attendance.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), null, Arg.Any<CancellationToken>())
            .Returns(new[] { AttendanceRecord.Record(tenantId, employee.Id, null, AttendanceEventType.ClockIn, new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeSpan.Zero)) });
        var employees = Substitute.For<IEmployeeRepository>();
        employees.ListAsync(Arg.Any<CancellationToken>()).Returns(new[] { employee });
        var contracts = Substitute.For<IEmploymentContractRepository>();
        contracts.ListAsync(employee.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<EmploymentContract>());

        await FluentActions.Invoking(() => new GeneratePayrollExportQueryHandler(employees, contracts, attendance)
            .HandleAsync(new GeneratePayrollExportQuery(new DateOnly(2026, 9, 13), new DateOnly(2026, 9, 13))))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Csv_export_is_deterministic_and_escapes_fields()
    {
        var row = new PayrollExportRow(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "E,2026", 7m, 100m, 700m, "ZAR");

        PayrollExportCsv.Serialize([row]).Should().Be(
            "employee_id,employee_number,hours,hourly_rate,gross_amount,currency\r\n" +
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa,\"E,2026\",7,100,700,ZAR\r\n");
    }
}
