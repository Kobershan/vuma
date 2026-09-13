using VumaRetail.Domain.HrManagement;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Hr;

namespace VumaRetail.UnitTests.Hr;

public sealed class DisciplinaryCaseTests
{
    [Fact]
    public void Case_requires_investigation_before_one_way_decision()
    {
        var opened = new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeSpan.Zero);
        var @case = DisciplinaryCase.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 12), "Late arrival", opened);

        FluentActions.Invoking(() => @case.Decide("Written warning", opened.AddHours(1)))
            .Should().Throw<InvalidOperationException>();
        @case.StartInvestigation(opened.AddHours(1));
        @case.Decide("Written warning", opened.AddHours(2));

        @case.Status.Should().Be(DisciplinaryCaseStatus.Decided);
        @case.Decision.Should().Be("Written warning");
        FluentActions.Invoking(() => @case.Decide("Dismissed", opened.AddHours(3)))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Investigation_and_decision_cannot_predate_their_parent_event()
    {
        var opened = new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeSpan.Zero);
        var @case = DisciplinaryCase.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 12), "Unsafe conduct", opened);

        FluentActions.Invoking(() => @case.StartInvestigation(opened.AddMinutes(-1)))
            .Should().Throw<ArgumentException>();
        @case.StartInvestigation(opened);
        FluentActions.Invoking(() => @case.Decide("Final warning", opened.AddMinutes(-1)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Open_handler_requires_the_active_company_and_persists_the_case()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(tenantId, "E-100", "A", "Employee", DateTimeOffset.UtcNow, EmploymentType.Permanent);
        var employees = Substitute.For<IEmployeeRepository>();
        employees.FindAsync(employee.Id, Arg.Any<CancellationToken>()).Returns(employee);
        var cases = Substitute.For<IDisciplinaryCaseRepository>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeSpan.Zero));

        var id = await new OpenDisciplinaryCaseCommandHandler(employees, cases, tenant, company, clock)
            .HandleAsync(new OpenDisciplinaryCaseCommand(companyId, employee.Id, new DateOnly(2026, 9, 12), "Late arrival"));

        id.Should().NotBeEmpty();
        cases.Received(1).Add(Arg.Is<DisciplinaryCase>(x => x.Id == id && x.CompanyId == companyId));
    }
}
