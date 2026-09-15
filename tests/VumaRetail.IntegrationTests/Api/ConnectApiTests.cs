using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Connect;
using VumaRetail.Domain.Connect;
using VumaRetail.Domain.Procurement;
using VumaRetail.Domain.Primitives;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>Stage 21b supplier-portal route and permission-boundary evidence.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ConnectApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Connect_openapi_contains_supplier_portal_surfaces()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        using JsonDocument document = JsonDocument.Parse(await harness.Client.GetStringAsync("/openapi/v1.json"));
        JsonElement paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/connect/connections", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/connect/connections/codes", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/connect/catalogue/publish", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/connect/price-lists/publish", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/connect/orders", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/connect/orders/{id}/asn", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/connect/payments/settle", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/connect/payments/{paymentId}/remittance", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/connect/connections/{id}/granted-users", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Connect_viewer_cannot_mutate_supplier_portal()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("connect-viewer", "CorrectHorseBattery1", ConnectPermissions.View);
        using HttpClient client = await harness.SignInAsync("connect-viewer");

        HttpResponseMessage code = await client.PostAsJsonAsync("/api/v1/connect/connections/codes", new
        {
            Code = "DENIED-CODE", Uses = 1, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            PriceTier = (string?)null, Territory = (string?)null, GrantsPortalAccess = false
        });
        HttpResponseMessage catalogue = await client.PostAsJsonAsync("/api/v1/connect/catalogue/publish", new
        {
            ConnectionId = Guid.NewGuid(), Version = 1, EffectiveFrom = DateTimeOffset.UtcNow,
            VersionNote = "denied", Lines = Array.Empty<object>()
        });
        HttpResponseMessage order = await client.PostAsJsonAsync("/api/v1/connect/orders", new
        {
            ConnectionId = Guid.NewGuid(), PurchaseOrderId = Guid.NewGuid(), OrderNumber = "DENIED",
            Lines = Array.Empty<object>()
        });

        code.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        catalogue.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        order.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Retailer_can_read_connection_portal_grants_over_the_real_api()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        Guid supplierTenant = Guid.NewGuid();
        Guid contactId = Guid.NewGuid();
        Guid connectionId = await harness.InScopeAsync(async services =>
        {
            VumaRetailDbContext context = services.GetRequiredService<VumaRetailDbContext>();
            TradingConnection connection = TradingConnection.Request(
                supplierTenant, harness.TenantId, "SUP-API", "RET-API", DateTimeOffset.UtcNow);
            connection.Accept("ZAR", 25_000m, 5, 100m, DateTimeOffset.UtcNow);
            context.Add(connection);
            context.Add(SupplierPortalGrant.Create(
                supplierTenant, harness.TenantId, connection.Id, contactId, "orders", DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
            return connection.Id;
        });

        await harness.CreateUserAsync("portal-reader", permissions: ConnectPermissions.View);
        using HttpClient client = await harness.SignInAsync("portal-reader");

        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/connect/connections/{connectionId}/granted-users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement grants = await response.Content.ReadFromJsonAsync<JsonElement>();
        grants.GetArrayLength().Should().Be(1);
        grants[0].GetProperty("accessRole").GetString().Should().Be("orders");
    }

    [Fact]
    public async Task Dispatched_asn_creates_one_draft_receipt_when_the_request_is_replayed()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        Guid supplierTenant = Guid.NewGuid();
        Guid purchaseOrderId = Guid.NewGuid();
        Guid connectOrderId = Guid.NewGuid();
        Guid purchaseOrderLineId = Guid.Empty;

        await harness.InScopeAsync<object?>(async services =>
        {
            VumaRetailDbContext context = services.GetRequiredService<VumaRetailDbContext>();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            PurchaseOrder purchaseOrder = PurchaseOrder.Raise(
                harness.TenantId, harness.StoreId, "PO-CONNECT-RECEIPT", Guid.NewGuid(), "ZAR",
                Guid.NewGuid(), DateOnly.FromDateTime(now.UtcDateTime.AddDays(7)), null, null, now);
            purchaseOrderId = purchaseOrder.Id;
            PurchaseOrderLine line = purchaseOrder.AddLine(
                Guid.NewGuid(), null, "Connect milk", new Quantity(5, "EA"), new Money(10, "ZAR"),
                "STANDARD", new Money(10, "ZAR"), Money.Zero("ZAR"), null);
            purchaseOrder.Approve(Guid.NewGuid(), now);
            purchaseOrder.Issue(now);

            TradingConnection connection = TradingConnection.Request(
                supplierTenant, harness.TenantId, "SUP-RECEIPT", "RET-RECEIPT", now);
            connection.Accept("ZAR", 25_000m, 5, 100m, now);
            ConnectOrder connectOrder = ConnectOrder.Place(
                harness.TenantId, supplierTenant, connection.Id, purchaseOrder.Id, "PO-CONNECT-RECEIPT", now);
            connectOrder.AddLine(
                "MILK-1L", "Connect milk", new Quantity(5, "EA"), new Money(10, "ZAR"), line.Id);
            connectOrder.Confirm(
                new Dictionary<Guid, Quantity> { [connectOrder.Lines.Single().Id] = new Quantity(5, "EA") },
                now.AddDays(3));
            connectOrder.Dispatch(
                "DN-CONNECT-RECEIPT",
                new Dictionary<Guid, Quantity> { [connectOrder.Lines.Single().Id] = new Quantity(5, "EA") }, now);

            context.AddRange(purchaseOrder, connection, connectOrder);
            await context.SaveChangesAsync();
            purchaseOrderLineId = line.Id;
            connectOrderId = connectOrder.Id;
            return null;
        });

        await harness.CreateUserAsync("connect-receiver", permissions: ConnectPermissions.Order);
        using HttpClient client = await harness.SignInAsync("connect-receiver");
        HttpResponseMessage first = await client.PostAsJsonAsync(
            $"/api/v1/connect/orders/{connectOrderId}/asn/receipt", new { DeliveryNoteNumber = "DN-CONNECT-RECEIPT" });
        HttpResponseMessage replay = await client.PostAsJsonAsync(
            $"/api/v1/connect/orders/{connectOrderId}/asn/receipt", new { DeliveryNoteNumber = "DN-CONNECT-RECEIPT" });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        await harness.InScopeAsync<object?>(async services =>
        {
            VumaRetailDbContext context = services.GetRequiredService<VumaRetailDbContext>();
            (await context.GoodsReceipts.CountAsync(receipt =>
                receipt.PurchaseOrderId == purchaseOrderId && receipt.DeliveryNoteNumber == "DN-CONNECT-RECEIPT"))
                .Should().Be(1);
            (await context.GoodsReceiptLines.CountAsync(line => line.PurchaseOrderLineId == purchaseOrderLineId))
                .Should().Be(1);
            return null;
        });
    }

    [Fact]
    public async Task Settlement_replay_returns_the_same_remittance_and_supplier_can_read_it()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        Guid supplierTenant = Guid.NewGuid();
        Guid connectionId = await harness.InScopeAsync(async services =>
        {
            VumaRetailDbContext context = services.GetRequiredService<VumaRetailDbContext>();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            TradingConnection connection = TradingConnection.Request(
                supplierTenant, harness.TenantId, "SUP-SETTLE", "RET-SETTLE", now);
            connection.Accept("ZAR", 25_000m, 5, 100m, now);
            context.Add(connection);
            await context.SaveChangesAsync();
            return connection.Id;
        });

        await harness.CreateUserAsync(
            "settlement-operator", permissions: [ConnectPermissions.Order, ConnectPermissions.View]);
        using HttpClient client = await harness.SignInAsync("settlement-operator");
        Guid paymentId = Guid.NewGuid();
        object request = new
        {
            PaymentId = paymentId, ConnectionId = connectionId, InvoiceReference = "INV-CONNECT-API",
            Amount = 125.50m, Currency = "ZAR", Method = ConnectPaymentMethod.Eft
        };

        HttpResponseMessage first = await client.PostAsJsonAsync("/api/v1/connect/payments/settle", request);
        JsonElement firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        HttpResponseMessage replay = await client.PostAsJsonAsync("/api/v1/connect/payments/settle", request);
        JsonElement replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        replayBody.GetProperty("remittanceReference").GetString()
            .Should().Be(firstBody.GetProperty("remittanceReference").GetString());

        HttpResponseMessage remittance = await client.GetAsync(
            $"/api/v1/connect/payments/{paymentId}/remittance");
        remittance.StatusCode.Should().Be(HttpStatusCode.OK);
        (await remittance.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("invoiceReference").GetString().Should().Be("INV-CONNECT-API");
    }
}
