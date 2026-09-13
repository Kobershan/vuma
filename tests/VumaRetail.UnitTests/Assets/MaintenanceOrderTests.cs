using VumaRetail.Domain.Assets;

namespace VumaRetail.UnitTests.Assets;

public sealed class MaintenanceOrderTests
{
    [Fact]
    public void Maintenance_order_requires_start_before_completion()
    {
        var order = MaintenanceOrder.Create(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "Replace till drawer");

        FluentActions.Invoking(() => order.Complete(DateTimeOffset.UtcNow))
            .Should().Throw<InvalidOperationException>();
        order.Start();
        order.Complete(DateTimeOffset.UtcNow);

        order.Status.Should().Be(MaintenanceOrderStatus.Completed);
        order.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Completed_maintenance_order_cannot_be_cancelled()
    {
        var order = MaintenanceOrder.Create(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "Inspect safe");
        order.Start();
        order.Complete(DateTimeOffset.UtcNow);

        FluentActions.Invoking(order.Cancel).Should().Throw<InvalidOperationException>();
    }
}
