#pragma warning disable CS1591
using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Hr;
using VumaRetail.Domain.HrManagement;
using VumaRetail.Domain.HrWorkforce;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales.Analytics;

namespace VumaRetail.UnitTests.Hr;

public sealed class WorkforceLabourCostQueryTests
{
    [Fact]
    public async Task Report_compares_worked_hours_and_cost_with_company_sales()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        DateOnly day = new(2026, 9, 1);
        Employee employee = Employee.Create(tenantId, "E-001", "Ada", "Lovelace",
            new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), EmploymentType.Permanent);
        employee.AssignCompany(companyId);
        EmploymentContract contract = EmploymentContract.Create(tenantId, employee.Id, day.AddDays(-1), null, 25m, "ZAR");
        DateTimeOffset start = new(day.ToDateTime(new TimeOnly(8)), TimeSpan.Zero);
        AttendanceRecord[] events =
        [
            AttendanceRecord.Record(tenantId, employee.Id, null, AttendanceEventType.ClockIn, start),
            AttendanceRecord.Record(tenantId, employee.Id, null, AttendanceEventType.ClockOut, start.AddHours(2)),
        ];
        SalesAnalytics salesRow = SalesAnalytics.Create(tenantId, null, companyId, AnalyticsPeriod.Daily,
            start.Date, start.Date.AddDays(1), null, "Till", "ZAR");
        salesRow.Aggregate(new Money(500m, "ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), 1, 1, start);

        var employees = Substitute.For<IEmployeeRepository>();
        employees.ListAsync(Arg.Any<CancellationToken>()).Returns(new[] { employee });
        var contracts = Substitute.For<IEmploymentContractRepository>();
        contracts.ListAsync(employee.Id, Arg.Any<CancellationToken>()).Returns(new[] { contract });
        var attendance = Substitute.For<IAttendanceRepository>();
        attendance.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), null, Arg.Any<CancellationToken>()).Returns(events);
        var sales = Substitute.For<ISalesAnalyticsRepository>();
        sales.GetByCompanyAsync(companyId, AnalyticsPeriod.Daily, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new[] { salesRow });
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);

        WorkforceLabourCostResult result = (await new GetWorkforceLabourCostQueryHandler(
            employees, contracts, attendance, sales, company, tenant)
            .HandleAsync(new GetWorkforceLabourCostQuery(companyId, day, day))).Should().ContainSingle().Subject;

        result.Hours.Should().Be(2m);
        result.LabourCost.Should().Be(50m);
        result.SalesRevenue.Should().Be(500m);
        result.LabourCostPercentageOfSales.Should().Be(10m);
    }

    [Fact]
    public async Task Report_does_not_divide_by_zero_and_excludes_other_company_sales()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        Guid otherCompanyId = Guid.NewGuid();
        DateOnly day = new(2026, 9, 1);
        Employee employee = Employee.Create(tenantId, "E-002", "Grace", "Hopper",
            new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), EmploymentType.Permanent);
        employee.AssignCompany(companyId);
        EmploymentContract contract = EmploymentContract.Create(tenantId, employee.Id, day, null, 20m, "ZAR");
        DateTimeOffset start = new(day.ToDateTime(new TimeOnly(9)), TimeSpan.Zero);
        var employees = Substitute.For<IEmployeeRepository>();
        employees.ListAsync(Arg.Any<CancellationToken>()).Returns(new[] { employee });
        var contracts = Substitute.For<IEmploymentContractRepository>();
        contracts.ListAsync(employee.Id, Arg.Any<CancellationToken>()).Returns(new[] { contract });
        var attendance = Substitute.For<IAttendanceRepository>();
        attendance.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), null, Arg.Any<CancellationToken>()).Returns(new[]
        {
            AttendanceRecord.Record(tenantId, employee.Id, null, AttendanceEventType.ClockIn, start),
            AttendanceRecord.Record(tenantId, employee.Id, null, AttendanceEventType.ClockOut, start.AddHours(1)),
        });
        SalesAnalytics other = SalesAnalytics.Create(tenantId, null, otherCompanyId, AnalyticsPeriod.Daily,
            start.Date, start.Date.AddDays(1), null, "Till", "ZAR");
        other.Aggregate(new Money(999m, "ZAR"), Money.Zero("ZAR"), Money.Zero("ZAR"), 1, 1, start);
        var sales = Substitute.For<ISalesAnalyticsRepository>();
        sales.GetByCompanyAsync(companyId, AnalyticsPeriod.Daily, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new[] { other });
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);

        WorkforceLabourCostResult result = (await new GetWorkforceLabourCostQueryHandler(
            employees, contracts, attendance, sales, company, tenant)
            .HandleAsync(new GetWorkforceLabourCostQuery(companyId, day, day))).Should().ContainSingle().Subject;

        result.SalesRevenue.Should().Be(0m);
        result.LabourCostPercentageOfSales.Should().Be(0m);
    }
}
