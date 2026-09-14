using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Manufacturing;
using VumaRetail.Domain.Manufacturing;

namespace VumaRetail.UnitTests.Manufacturing;

public sealed class ManufacturingCommandTests
{
    [Fact]
    public async Task Production_read_refuses_an_order_from_another_active_company()
    {
        IProductionOrderRepository repository = Substitute.For<IProductionOrderRepository>();
        ICompanyContext company = Substitute.For<ICompanyContext>();
        Guid orderCompany = Guid.NewGuid();
        company.CompanyId.Returns(Guid.NewGuid());
        ProductionOrder order = ProductionOrder.Create(Guid.NewGuid(), Guid.NewGuid(), orderCompany,
            Guid.NewGuid(), new(1m, "EA"), "PO-SCOPE", Guid.NewGuid());
        repository.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        Func<Task> action = () => new GetProductionOrderQueryHandler(repository, company)
            .HandleAsync(new GetProductionOrderQuery(order.Id));

        await action.Should().ThrowAsync<ManufacturingRuleException>();
    }

    [Fact]
    public async Task Create_production_command_rejects_changed_duplicate_payload()
    {
        IProductionOrderRepository repository = Substitute.For<IProductionOrderRepository>();
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());
        Guid bomId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        Guid finishedItemId = Guid.NewGuid();
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        ProductionOrder existing = ProductionOrder.Create(operationId, tenant.TenantId, companyId, finishedItemId, new(10m, "EA"), "PO-1", bomId);
        repository.FindAsync(operationId, Arg.Any<CancellationToken>()).Returns(existing);
        CreateProductionOrderCommand command = new(operationId, companyId, finishedItemId, 11m, "EA", "PO-1", bomId);

        Func<Task> action = () => new CreateProductionOrderCommandHandler(repository, tenant, company).HandleAsync(command);

        await action.Should().ThrowAsync<ManufacturingRuleException>();
        repository.DidNotReceive().Add(Arg.Any<ProductionOrder>());
    }

    [Fact]
    public async Task Create_production_command_rejects_a_company_that_is_not_active()
    {
        IProductionOrderRepository repository = Substitute.For<IProductionOrderRepository>();
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(Guid.NewGuid());
        CreateProductionOrderCommand command = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, "EA", "PO-1", Guid.NewGuid());

        Func<Task> action = () => new CreateProductionOrderCommandHandler(repository, tenant, company).HandleAsync(command);

        await action.Should().ThrowAsync<InvalidOperationException>();
        await repository.DidNotReceive().FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        repository.DidNotReceive().Add(Arg.Any<ProductionOrder>());
    }
    [Fact]
    public async Task Create_command_adds_a_draft_BOM_for_the_ambient_tenant()
    {
        IBillOfMaterialsRepository repository = Substitute.For<IBillOfMaterialsRepository>();
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());
        Guid itemId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        repository.FindVersionAsync(itemId, null, 1, Arg.Any<CancellationToken>()).Returns((BillOfMaterials?)null);
        CreateBillOfMaterialsCommand command = new(
            companyId, itemId, null, 1, "Assembly",
            [new BillOfMaterialsLineInput(Guid.NewGuid(), null, 2m, "EA")]);

        Guid id = await new CreateBillOfMaterialsCommandHandler(repository, tenant, company).HandleAsync(command);

        id.Should().NotBeEmpty();
        repository.Received(1).Add(Arg.Is<BillOfMaterials>(bom =>
            bom.TenantId == tenant.TenantId && bom.Status == BillOfMaterialsStatus.Draft && bom.Lines.Count == 1));
    }

    [Fact]
    public async Task Create_command_rejects_a_duplicate_version()
    {
        IBillOfMaterialsRepository repository = Substitute.For<IBillOfMaterialsRepository>();
        ITenantContext tenant = Substitute.For<ITenantContext>();
        ICompanyContext company = Substitute.For<ICompanyContext>();
        Guid companyId = Guid.NewGuid();
        company.CompanyId.Returns(companyId);
        BillOfMaterials existing = BillOfMaterials.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Existing");
        repository.FindVersionAsync(existing.FinishedItemId, null, 1, Arg.Any<CancellationToken>()).Returns(existing);
        CreateBillOfMaterialsCommand command = new(
            companyId, existing.FinishedItemId, null, 1, "Duplicate",
            [new BillOfMaterialsLineInput(Guid.NewGuid(), null, 1m, "EA")]);

        Func<Task> action = () => new CreateBillOfMaterialsCommandHandler(repository, tenant, company).HandleAsync(command);

        await action.Should().ThrowAsync<ManufacturingRuleException>();
        repository.DidNotReceive().Add(Arg.Any<BillOfMaterials>());
    }

    [Fact]
    public async Task Publish_command_transitions_the_loaded_draft()
    {
        IBillOfMaterialsRepository repository = Substitute.For<IBillOfMaterialsRepository>();
        Guid companyId = Guid.NewGuid();
        BillOfMaterials bom = BillOfMaterials.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Assembly");
        bom.AssignCompany(companyId);
        bom.AddLine(Guid.NewGuid(), new VumaRetail.Domain.Primitives.Quantity(1m, "EA"));
        repository.FindAsync(bom.Id, Arg.Any<CancellationToken>()).Returns(bom);
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);

        await new PublishBillOfMaterialsCommandHandler(repository, company).HandleAsync(new PublishBillOfMaterialsCommand(bom.Id));

        bom.Status.Should().Be(BillOfMaterialsStatus.Published);
    }

    [Fact]
    public async Task Publish_command_rejects_a_BOM_from_another_active_company()
    {
        IBillOfMaterialsRepository repository = Substitute.For<IBillOfMaterialsRepository>();
        BillOfMaterials bom = BillOfMaterials.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Assembly");
        bom.AssignCompany(Guid.NewGuid());
        repository.FindAsync(bom.Id, Arg.Any<CancellationToken>()).Returns(bom);
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(Guid.NewGuid());

        await FluentActions.Invoking(() => new PublishBillOfMaterialsCommandHandler(repository, company)
            .HandleAsync(new PublishBillOfMaterialsCommand(bom.Id)))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
