using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Projects;
using VumaRetail.Domain.Projects;

namespace VumaRetail.UnitTests.Projects;

public sealed class ProjectLifecycleTests
{
    [Fact]
    public void Project_moves_from_draft_through_active_to_closed()
    {
        Project project = Project.Create(Guid.NewGuid(), null, Guid.NewGuid(), "P-1", "Refit", "ZAR");
        project.Status.Should().Be(ProjectStatus.Draft);
        project.Activate();
        project.Status.Should().Be(ProjectStatus.Active);
        project.Close();
        project.Status.Should().Be(ProjectStatus.Closed);
        FluentActions.Invoking(project.Close).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Draft_project_cannot_close_before_activation()
    {
        Project project = Project.Create(Guid.NewGuid(), null, Guid.NewGuid(), "P-1", "Refit", "ZAR");
        FluentActions.Invoking(project.Close).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Activate_and_close_handlers_enforce_company_scope()
    {
        var tenant = Substitute.For<ITenantContext>();
        Guid tenantId = Guid.NewGuid();
        tenant.TenantId.Returns(tenantId);
        var company = Substitute.For<ICompanyContext>();
        Guid companyId = Guid.NewGuid();
        company.CompanyId.Returns(companyId);
        Project project = Project.Create(tenantId, null, companyId, "P-1", "Refit", "ZAR");
        var repository = Substitute.For<IProjectRepository>();
        repository.FindProjectAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);

        await new ActivateProjectCommandHandler(repository, company, tenant)
            .HandleAsync(new ActivateProjectCommand(companyId, project.Id));
        project.Status.Should().Be(ProjectStatus.Active);
        await new CloseProjectCommandHandler(repository, company, tenant)
            .HandleAsync(new CloseProjectCommand(companyId, project.Id));
        project.Status.Should().Be(ProjectStatus.Closed);

        var wrongCompany = Substitute.For<ICompanyContext>();
        wrongCompany.CompanyId.Returns(Guid.NewGuid());
        var act = () => new ActivateProjectCommandHandler(repository, wrongCompany, tenant)
            .HandleAsync(new ActivateProjectCommand(companyId, project.Id));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Get_and_list_queries_return_only_the_active_company_projects()
    {
        var company = Substitute.For<ICompanyContext>();
        Guid companyId = Guid.NewGuid();
        company.CompanyId.Returns(companyId);
        Guid tenantId = Guid.NewGuid();
        Project first = Project.Create(tenantId, null, companyId, "A-1", "Alpha", "ZAR");
        Project second = Project.Create(tenantId, null, companyId, "B-1", "Beta", "ZAR");
        var repository = Substitute.For<IProjectRepository>();
        repository.FindProjectAsync(first.Id, Arg.Any<CancellationToken>()).Returns(first);
        repository.ListProjectsAsync(companyId, Arg.Any<CancellationToken>()).Returns([first, second]);

        ProjectResult? single = await new GetProjectQueryHandler(repository, company)
            .HandleAsync(new GetProjectQuery(companyId, first.Id));
        single.Should().NotBeNull();
        single!.Code.Should().Be("A-1");

        IReadOnlyList<ProjectResult> all = await new ListProjectsQueryHandler(repository, company)
            .HandleAsync(new ListProjectsQuery(companyId));
        all.Select(p => p.Code).Should().Equal("A-1", "B-1");
    }
}
