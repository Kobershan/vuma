using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Assets;
using VumaRetail.Domain.Assets;

namespace VumaRetail.UnitTests.Assets;

public sealed class ChecklistCommandTests
{
    [Fact]
    public async Task Create_handler_persists_a_company_scoped_checklist()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var checklists = Substitute.For<IChecklistRepository>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);

        var id = await new CreateStoreChecklistCommandHandler(checklists, tenant, company)
            .HandleAsync(new CreateStoreChecklistCommand(companyId, Guid.NewGuid(), "opening", "Opening", ["cash", "safe"]));

        id.Should().NotBeEmpty();
        checklists.Received(1).Add(Arg.Is<StoreChecklist>(c =>
            c.Id == id && c.TenantId == tenantId && c.CompanyId == companyId && c.Code == "OPENING"));
    }

    [Fact]
    public async Task Submit_handler_replays_an_identical_operation_without_adding_again()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var checklist = StoreChecklist.Create(tenantId, null, companyId, "open", "Opening", ["cash"]);
        var operationId = Guid.NewGuid();
        var submitted = ChecklistExecution.Submit(tenantId, null, companyId, checklist.Id, operationId,
            "device-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "evidence/1.json");
        var checklists = Substitute.For<IChecklistRepository>();
        checklists.FindExecutionByOperationIdAsync(operationId, Arg.Any<CancellationToken>()).Returns(submitted);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);

        var id = await new SubmitChecklistExecutionCommandHandler(checklists, tenant, company)
            .HandleAsync(new SubmitChecklistExecutionCommand(companyId, null, checklist.Id, operationId,
                "device-1", submitted.CapturedAt, submitted.SubmittedAt, "evidence/1.json"));

        id.Should().Be(submitted.Id);
        checklists.DidNotReceive().Add(Arg.Any<ChecklistExecution>());
    }

    [Fact]
    public async Task Submit_handler_rejects_replay_with_changed_evidence()
    {
        var companyId = Guid.NewGuid();
        var checklistId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var existing = ChecklistExecution.Submit(Guid.NewGuid(), null, companyId, checklistId, operationId,
            "device-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "evidence/original.json");
        var checklists = Substitute.For<IChecklistRepository>();
        checklists.FindExecutionByOperationIdAsync(operationId, Arg.Any<CancellationToken>()).Returns(existing);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(existing.TenantId);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);

        await FluentActions.Invoking(() => new SubmitChecklistExecutionCommandHandler(checklists, tenant, company)
            .HandleAsync(new SubmitChecklistExecutionCommand(companyId, null, checklistId, operationId,
                "device-1", existing.CapturedAt, existing.SubmittedAt, "evidence/changed.json")))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Submit_handler_rejects_replay_with_changed_device_or_capture_time()
    {
        var companyId = Guid.NewGuid();
        var checklistId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var captured = DateTimeOffset.UtcNow;
        var existing = ChecklistExecution.Submit(Guid.NewGuid(), Guid.NewGuid(), companyId, checklistId, operationId,
            "device-1", captured, captured.AddMinutes(1), "evidence/original.json");
        var checklists = Substitute.For<IChecklistRepository>();
        checklists.FindExecutionByOperationIdAsync(operationId, Arg.Any<CancellationToken>()).Returns(existing);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(existing.TenantId);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);

        await FluentActions.Invoking(() => new SubmitChecklistExecutionCommandHandler(checklists, tenant, company)
            .HandleAsync(new SubmitChecklistExecutionCommand(companyId, existing.StoreId, checklistId, operationId,
                "device-2", captured.AddSeconds(1), existing.SubmittedAt, existing.EvidenceReference)))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
