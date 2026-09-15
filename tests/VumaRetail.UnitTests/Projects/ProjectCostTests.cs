using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Projects;
using VumaRetail.Application.Hr;
using VumaRetail.Domain.HrManagement;
using VumaRetail.Domain.HrWorkforce;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Projects;

namespace VumaRetail.UnitTests.Projects;

public sealed class ProjectCostTests
{
    [Fact]
    public void Reversal_is_a_new_negative_entry_and_preserves_original()
    {
        ProjectCostEntry original = ProjectCostEntry.Record(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "TIMESHEET-1", ProjectCostKind.Labour, new Money(200m, "ZAR"));
        ProjectCostEntry reversal = ProjectCostEntry.Reverse(original, "REVERSAL-1");
        original.Amount.Amount.Should().Be(200m);
        reversal.Amount.Amount.Should().Be(-200m);
        reversal.ReversesEntryId.Should().Be(original.Id);
    }

    [Fact]
    public async Task Cost_allocation_is_idempotent_and_rejects_changed_replay_content()
    {
        var tenant = Substitute.For<ITenantContext>();
        Guid tenantId = Guid.NewGuid();
        tenant.TenantId.Returns(tenantId);
        var company = Substitute.For<ICompanyContext>();
        var companyId = Guid.NewGuid();
        company.CompanyId.Returns(companyId);
        var project = Project.Create(tenantId, null, companyId, "P-1", "Refit", "ZAR");
        var repository = Substitute.For<IProjectRepository>();
        repository.FindProjectAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        var source = "TIMESHEET-42";
        var existing = ProjectCostEntry.Record(tenant.TenantId, null, companyId, project.Id, source,
            ProjectCostKind.Labour, new Money(200m, "ZAR"));
        repository.FindCostBySourceAsync(project.Id, source, Arg.Any<CancellationToken>()).Returns(existing);
        var handler = new AllocateProjectCostCommandHandler(repository, tenant, company);

        var same = await handler.HandleAsync(new AllocateProjectCostCommand(companyId, project.Id, source,
            ProjectCostKind.Labour, 200m, "ZAR"));
        same.Should().Be(existing.Id);

        var changed = () => handler.HandleAsync(new AllocateProjectCostCommand(companyId, project.Id, source,
            ProjectCostKind.Labour, 250m, "ZAR"));
        await changed.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Cost_summary_is_company_scoped_and_keeps_currencies_separate()
    {
        var tenant = Substitute.For<ITenantContext>();
        Guid tenantId = Guid.NewGuid();
        tenant.TenantId.Returns(tenantId);
        var company = Substitute.For<ICompanyContext>();
        var companyId = Guid.NewGuid();
        company.CompanyId.Returns(companyId);
        var project = Project.Create(tenantId, null, companyId, "P-1", "Refit", "ZAR");
        var repository = Substitute.For<IProjectRepository>();
        repository.FindProjectAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        ProjectCostEntry original = ProjectCostEntry.Record(tenantId, null, companyId, project.Id, "A", ProjectCostKind.Other, new Money(25m, "ZAR"));
        repository.ListCostsAsync(project.Id, Arg.Any<CancellationToken>()).Returns([
            ProjectCostEntry.Record(tenantId, null, companyId, project.Id, "B", ProjectCostKind.Other, new Money(100m, "ZAR")),
            ProjectCostEntry.Record(tenantId, null, companyId, project.Id, "C", ProjectCostKind.Other, new Money(10m, "USD")),
            ProjectCostEntry.Reverse(original, "D")]);

        ProjectCostSummaryResult result = (await new GetProjectCostSummaryQueryHandler(repository, company)
            .HandleAsync(new GetProjectCostSummaryQuery(companyId, project.Id)))!;

        result.EntryCount.Should().Be(3);
        result.Totals.Should().ContainInOrder(new ProjectCostTotal("USD", 10m), new ProjectCostTotal("ZAR", 75m));
    }

    [Fact]
    public async Task Cost_allocation_rejects_a_project_from_another_tenant()
    {
        Guid activeTenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(activeTenantId);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        Project project = Project.Create(Guid.NewGuid(), null, companyId, "P-foreign", "Foreign", "ZAR");
        var repository = Substitute.For<IProjectRepository>();
        repository.FindProjectAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);

        await FluentActions.Invoking(() => new AllocateProjectCostCommandHandler(repository, tenant, company)
            .HandleAsync(new AllocateProjectCostCommand(companyId, project.Id, "foreign", ProjectCostKind.Other, 1m, "ZAR")))
            .Should().ThrowAsync<InvalidOperationException>();
        repository.DidNotReceive().Add(Arg.Any<ProjectCostEntry>());
    }

    [Fact]
    public async Task Labour_cost_allocation_prices_closed_attendance_and_replays_by_period()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        DateOnly day = new(2026, 9, 1);
        DateTimeOffset start = new(day.ToDateTime(new TimeOnly(8)), TimeSpan.Zero);
        Employee employee = Employee.Create(tenantId, "E-100", "Ada", "Lovelace", start, EmploymentType.Permanent);
        employee.AssignCompany(companyId);
        EmploymentContract contract = EmploymentContract.Create(tenantId, employee.Id, day, null, 30m, "ZAR");
        Project project = Project.Create(tenantId, null, companyId, "P-2", "Refit", "ZAR");
        var projects = Substitute.For<IProjectRepository>();
        projects.FindProjectAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        var employees = Substitute.For<IEmployeeRepository>();
        employees.FindAsync(employee.Id, Arg.Any<CancellationToken>()).Returns(employee);
        var contracts = Substitute.For<IEmploymentContractRepository>();
        contracts.ListAsync(employee.Id, Arg.Any<CancellationToken>()).Returns(new[] { contract });
        var attendance = Substitute.For<IAttendanceRepository>();
        attendance.ListAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), employee.Id, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                AttendanceRecord.Record(tenantId, employee.Id, null, AttendanceEventType.ClockIn, start),
                AttendanceRecord.Record(tenantId, employee.Id, null, AttendanceEventType.ClockOut, start.AddHours(3)),
            });
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);

        Guid id = await new AllocateProjectLabourCostCommandHandler(projects, employees, contracts,
            attendance, tenant, company).HandleAsync(new AllocateProjectLabourCostCommand(
            companyId, project.Id, employee.Id, day, day));

        projects.Received(1).Add(Arg.Is<ProjectCostEntry>(x => x.Kind == ProjectCostKind.Labour
            && x.Amount == new Money(90m, "ZAR")
            && x.SourceReference == $"labour:{employee.Id:D}:2026-09-01:2026-09-01"));
        id.Should().NotBeEmpty();
    }
}
