#pragma warning disable CS1591
using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Connect;
using VumaRetail.Domain.Connect;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Connect;

public sealed class ConnectPortalQueryTests
{
    [Fact]
    public async Task Asn_query_returns_only_a_dispatched_order_for_a_party_tenant()
    {
        Guid retailer = Guid.NewGuid();
        Guid supplier = Guid.NewGuid();
        ConnectOrder order = ConnectOrder.Place(retailer, supplier, Guid.NewGuid(), Guid.NewGuid(), "PO-1", DateTimeOffset.UtcNow);
        ConnectOrderLine line = order.AddLine("SKU-1", "Milk", new Quantity(2, "EA"), new Money(10, "ZAR"));
        order.Confirm(new Dictionary<Guid, Quantity> { [line.Id] = new(2, "EA") }, DateTimeOffset.UtcNow.AddDays(1));
        order.Dispatch("ASN-1", new Dictionary<Guid, Quantity> { [line.Id] = new(2, "EA") }, DateTimeOffset.UtcNow);

        IConnectOrderRepository orders = Substitute.For<IConnectOrderRepository>();
        orders.FindForTenantAsync(order.Id, retailer, Arg.Any<CancellationToken>()).Returns(order);
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(retailer);

        ConnectOrderResult? result = await new GetConnectAsnQueryHandler(orders, tenant)
            .HandleAsync(new GetConnectAsnQuery(order.Id));

        result.Should().NotBeNull();
        result!.DispatchNoteNumber.Should().Be("ASN-1");
        result.Lines.Single().DispatchedQuantity.Should().Be(2);
    }

    [Fact]
    public async Task Asn_query_hides_orders_before_dispatch()
    {
        Guid retailer = Guid.NewGuid();
        ConnectOrder order = ConnectOrder.Place(retailer, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "PO-2", DateTimeOffset.UtcNow);
        order.AddLine("SKU-2", "Bread", new Quantity(1, "EA"), new Money(8, "ZAR"));

        IConnectOrderRepository orders = Substitute.For<IConnectOrderRepository>();
        orders.FindForTenantAsync(order.Id, retailer, Arg.Any<CancellationToken>()).Returns(order);
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(retailer);

        (await new GetConnectAsnQueryHandler(orders, tenant).HandleAsync(new GetConnectAsnQuery(order.Id)))
            .Should().BeNull();
    }
}
