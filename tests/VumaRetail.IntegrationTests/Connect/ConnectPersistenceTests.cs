using Microsoft.EntityFrameworkCore;
using VumaRetail.Domain.Connect;
using VumaRetail.Domain.Primitives;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Connect;

[Collection(PostgresCollection.Name)]
public sealed class ConnectPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Connection_and_order_round_trip_is_scoped_to_the_two_parties()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid supplier = UuidV7.NewGuid();
        Guid retailer = UuidV7.NewGuid();
        Guid outsider = UuidV7.NewGuid();
        Guid connectionId;
        Guid orderId;
        Guid paymentId;

        await using (VumaRetailDbContext context = TestDbContextFactory.For(connectionString))
        {
            await context.Database.MigrateAsync();
            TradingConnection connection = TradingConnection.Request(
                supplier, retailer, "SUP-001", "RET-001", DateTimeOffset.UtcNow);
            connection.Accept("ZAR", 100_000m, 3, 500m, DateTimeOffset.UtcNow);
            ConnectOrder order = ConnectOrder.Place(
                retailer, supplier, connection.Id, UuidV7.NewGuid(), "PO-CONNECT-001", DateTimeOffset.UtcNow);
            ConnectOrderLine line = order.AddLine("MILK-2L", "Full cream milk", new Quantity(10m, "EA"), new Money(20m, "ZAR"));
            order.Confirm(new Dictionary<Guid, Quantity> { [line.Id] = new Quantity(10m, "EA") }, DateTimeOffset.UtcNow.AddDays(3));
            order.Dispatch("ASN-CONNECT-001", new Dictionary<Guid, Quantity> { [line.Id] = new Quantity(10m, "EA") }, DateTimeOffset.UtcNow);
            CataloguePublication publication = CataloguePublication.Publish(
                supplier, connection.Id, 1, DateTimeOffset.UtcNow, "Initial catalogue");
            publication.AddLine("MILK-2L", "Full cream milk", "6009880123456", 1, 1m, 3);
            PriceProposal proposal = PriceProposal.Publish(
                supplier, connection.Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), "March prices");
            proposal.AddLine("MILK-2L", 20m, "ZAR", 1m, 3);
            paymentId = UuidV7.NewGuid();
            ConnectRemittanceAdvice remittance = ConnectRemittanceAdvice.Issue(
                retailer, supplier, connection.Id, paymentId, "INV-CONNECT-001",
                new Money(200m, "ZAR"), ConnectPaymentMethod.Eft, "PROVIDER-1", "REM-CONNECT-001", DateTimeOffset.UtcNow);
            context.AddRange(connection, order, publication, proposal, remittance);
            await context.SaveChangesAsync();
            connectionId = connection.Id;
            orderId = order.Id;
        }

        await using (VumaRetailDbContext context = TestDbContextFactory.For(connectionString))
        {
            TradingConnectionRepository connections = new(context);
            ConnectOrderRepository orders = new(context);
            ConnectRemittanceRepository remittances = new(context);

            (await connections.FindForTenantAsync(connectionId, supplier)).Should().NotBeNull();
            (await connections.FindForTenantAsync(connectionId, retailer)).Should().NotBeNull();
            (await connections.FindForTenantAsync(connectionId, outsider)).Should().BeNull();
            (await orders.FindForTenantAsync(orderId, supplier)).Should().NotBeNull();
            (await orders.FindForTenantAsync(orderId, retailer)).Should().NotBeNull();
            (await orders.FindForTenantAsync(orderId, outsider)).Should().BeNull();
            (await context.CataloguePublications.CountAsync(x => x.ConnectionId == connectionId)).Should().Be(1);
            (await context.PriceProposals.CountAsync(x => x.ConnectionId == connectionId)).Should().Be(1);
            (await remittances.FindSettlementAsync(paymentId, retailer)).Should().NotBeNull();
            (await remittances.FindSettlementAsync(paymentId, supplier)).Should().BeNull();
            (await remittances.FindSettlementAsync(paymentId, outsider)).Should().BeNull();
        }
    }
}
