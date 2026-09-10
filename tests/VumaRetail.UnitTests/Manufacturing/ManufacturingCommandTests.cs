using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Manufacturing;
using VumaRetail.Domain.Manufacturing;

namespace VumaRetail.UnitTests.Manufacturing;

public sealed class ManufacturingCommandTests
{
    [Fact]
    public async Task Create_command_adds_a_draft_BOM_for_the_ambient_tenant()
    {
        IBillOfMaterialsRepository repository = Substitute.For<IBillOfMaterialsRepository>();
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());
        Guid itemId = Guid.NewGuid();
        repository.FindVersionAsync(itemId, null, 1, Arg.Any<CancellationToken>()).Returns((BillOfMaterials?)null);
        CreateBillOfMaterialsCommand command = new(
            Guid.NewGuid(), itemId, null, 1, "Assembly",
            [new BillOfMaterialsLineInput(Guid.NewGuid(), null, 2m, "EA")]);

        Guid id = await new CreateBillOfMaterialsCommandHandler(repository, tenant).HandleAsync(command);

        id.Should().NotBeEmpty();
        repository.Received(1).Add(Arg.Is<BillOfMaterials>(bom =>
            bom.TenantId == tenant.TenantId && bom.Status == BillOfMaterialsStatus.Draft && bom.Lines.Count == 1));
    }

    [Fact]
    public async Task Create_command_rejects_a_duplicate_version()
    {
        IBillOfMaterialsRepository repository = Substitute.For<IBillOfMaterialsRepository>();
        ITenantContext tenant = Substitute.For<ITenantContext>();
        BillOfMaterials existing = BillOfMaterials.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Existing");
        repository.FindVersionAsync(existing.FinishedItemId, null, 1, Arg.Any<CancellationToken>()).Returns(existing);
        CreateBillOfMaterialsCommand command = new(
            Guid.NewGuid(), existing.FinishedItemId, null, 1, "Duplicate",
            [new BillOfMaterialsLineInput(Guid.NewGuid(), null, 1m, "EA")]);

        Func<Task> action = () => new CreateBillOfMaterialsCommandHandler(repository, tenant).HandleAsync(command);

        await action.Should().ThrowAsync<ManufacturingRuleException>();
        repository.DidNotReceive().Add(Arg.Any<BillOfMaterials>());
    }

    [Fact]
    public async Task Publish_command_transitions_the_loaded_draft()
    {
        IBillOfMaterialsRepository repository = Substitute.For<IBillOfMaterialsRepository>();
        BillOfMaterials bom = BillOfMaterials.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Assembly");
        bom.AddLine(Guid.NewGuid(), new VumaRetail.Domain.Primitives.Quantity(1m, "EA"));
        repository.FindAsync(bom.Id, Arg.Any<CancellationToken>()).Returns(bom);

        await new PublishBillOfMaterialsCommandHandler(repository).HandleAsync(new PublishBillOfMaterialsCommand(bom.Id));

        bom.Status.Should().Be(BillOfMaterialsStatus.Published);
    }
}
