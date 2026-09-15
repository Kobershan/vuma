using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Connect;
using VumaRetail.Domain.Connect;
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
}
