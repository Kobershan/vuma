using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Projects;
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
}
