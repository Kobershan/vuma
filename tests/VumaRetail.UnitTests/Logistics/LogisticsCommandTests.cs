using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Logistics;
using VumaRetail.Domain.Logistics;

namespace VumaRetail.UnitTests.Logistics;

public sealed class LogisticsCommandTests
{
    [Fact]
    public async Task Refused_pod_moves_shipment_to_exception_not_delivered()
    {
        Guid tenantId = Guid.NewGuid();
        Shipment shipment = Shipment.Create(tenantId, Guid.NewGuid(), "S-1", null, null, null, null, "1 Main", null, "Durban", "4001", "ZA");
        ILogisticsRepository repository = Substitute.For<ILogisticsRepository>();
        repository.FindShipmentAsync(shipment.Id, Arg.Any<CancellationToken>()).Returns(shipment);
        repository.FindPodAsync(shipment.Id, Arg.Any<CancellationToken>()).Returns((ProofOfDelivery?)null);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var handler = new LogisticsCommandHandler(repository, tenant, Substitute.For<IClock>());

        await handler.HandleAsync(new RecordPodCommand(shipment.Id, null, ProofOfDeliveryOutcome.Refused, "Recipient", null, null, null, null, DateTimeOffset.UtcNow, null));

        shipment.Status.Should().Be(LogisticsShipmentStatus.Exception);
    }

    [Fact]
    public async Task A_stop_from_another_shipment_cannot_be_used_for_pod()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Shipment shipment = Shipment.Create(tenantId, storeId, "S-2", null, null, null, null, "1 Main", null, "Durban", "4001", "ZA");
        DeliveryStop stop = DeliveryStop.Create(tenantId, storeId, Guid.NewGuid(), Guid.NewGuid(), 1, "2 Main", "Durban");
        ILogisticsRepository repository = Substitute.For<ILogisticsRepository>();
        repository.FindShipmentAsync(shipment.Id, Arg.Any<CancellationToken>()).Returns(shipment);
        repository.FindPodAsync(shipment.Id, Arg.Any<CancellationToken>()).Returns((ProofOfDelivery?)null);
        repository.FindStopAsync(stop.Id, Arg.Any<CancellationToken>()).Returns(stop);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var handler = new LogisticsCommandHandler(repository, tenant, Substitute.For<IClock>());

        Func<Task> action = () => handler.HandleAsync(new RecordPodCommand(shipment.Id, stop.Id, ProofOfDeliveryOutcome.Delivered, "Recipient", null, null, null, null, DateTimeOffset.UtcNow, null));

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("Stop does not belong to shipment.");
    }

    [Fact]
    public async Task A_shipment_cannot_be_added_twice_to_a_run_or_from_another_store()
    {
        Guid tenantId = Guid.NewGuid();
        DeliveryRun run = DeliveryRun.Create(tenantId, Guid.NewGuid(), null, "RUN-1", new DateOnly(2026, 9, 14), null, null);
        Shipment shipment = Shipment.Create(tenantId, Guid.NewGuid(), "S-3", null, null, null, null, "1 Main", null, "Durban", "4001", "ZA");
        ILogisticsRepository repository = Substitute.For<ILogisticsRepository>();
        repository.FindRunAsync(run.Id, Arg.Any<CancellationToken>()).Returns(run);
        repository.FindShipmentAsync(shipment.Id, Arg.Any<CancellationToken>()).Returns(shipment);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var handler = new LogisticsCommandHandler(repository, tenant, Substitute.For<IClock>());

        Func<Task> action = () => handler.HandleAsync(new AddDeliveryStopCommand(run.Id, shipment.Id, 1));

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("Run and shipment must belong to the same store.");
        repository.DidNotReceive().Add(Arg.Any<DeliveryStop>());
    }
}
