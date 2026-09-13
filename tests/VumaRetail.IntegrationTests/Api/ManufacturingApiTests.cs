using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Manufacturing;
using VumaRetail.Contracts.Manufacturing;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>Stage 16 BOM API behavior and permission-boundary tests.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ManufacturingApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Authorized_user_can_create_read_and_publish_a_bom()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("bom-manager", "CorrectHorseBattery1", ManufacturingPermissions.Manage, ManufacturingPermissions.View);
        using HttpClient client = await harness.SignInAsync("bom-manager");

        Guid finishedItemId = Guid.NewGuid();
        CreateBillOfMaterialsRequest request = new(
            Guid.NewGuid(),
            finishedItemId,
            null,
            1,
            "Starter kit",
            [new BillOfMaterialsLineRequest(Guid.NewGuid(), null, 2m, "EA", 5m)]);

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/v1/manufacturing/boms/", request);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        BillOfMaterialsIdResponse response = (await created.Content.ReadFromJsonAsync<BillOfMaterialsIdResponse>())!;

        BillOfMaterialsResponse draft = await client.GetFromJsonAsync<BillOfMaterialsResponse>(
            $"/api/v1/manufacturing/boms/{response.Id:D}") ?? throw new InvalidOperationException("Missing BOM response.");
        draft.Status.Should().Be("Draft");
        draft.Lines.Should().ContainSingle();

        HttpResponseMessage published = await client.PostAsync(
            $"/api/v1/manufacturing/boms/{response.Id:D}/publish", content: null);
        published.StatusCode.Should().Be(HttpStatusCode.NoContent);

        BillOfMaterialsResponse result = await client.GetFromJsonAsync<BillOfMaterialsResponse>(
            $"/api/v1/manufacturing/boms/{response.Id:D}") ?? throw new InvalidOperationException("Missing published BOM response.");
        result.Status.Should().Be("Published");
    }

    [Fact]
    public async Task User_without_bom_permission_is_forbidden()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("bom-reader", "CorrectHorseBattery1", ManufacturingPermissions.View);
        using HttpClient client = await harness.SignInAsync("bom-reader");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/manufacturing/boms/",
            new CreateBillOfMaterialsRequest(
                Guid.NewGuid(), Guid.NewGuid(), null, 1, "Denied", [new BillOfMaterialsLineRequest(Guid.NewGuid(), null, 1m, "EA")]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task User_without_manufacturing_manage_permission_is_forbidden_for_production_create()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("production-viewer", "CorrectHorseBattery1", ManufacturingPermissions.View);
        using HttpClient client = await harness.SignInAsync("production-viewer");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/manufacturing/production-orders/",
            new CreateProductionOrderRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, "EA", "PROD-DENIED", Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authorized_user_can_execute_one_production_order_and_read_its_genealogy()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture, configureServices: StubCompanyRouting);
        await harness.CreateUserAsync("production-manager", "CorrectHorseBattery1", ManufacturingPermissions.Manage, ManufacturingPermissions.View);
        using HttpClient client = await harness.SignInAsync("production-manager");

        Guid companyId;
        Guid finishedItemId;
        Guid componentItemId;
        companyId = await harness.InScopeAsync(async services =>
        {
            VumaRegistryDbContext registry = services.GetRequiredService<VumaRegistryDbContext>();
            Company company = Company.Create(harness.TenantId, "PROD", "Production Company", "Production Company", "ZAR", "en-ZA", "PROD");
            company.SetConnectionSecretRef("test://company");
            company.SetMigration(1, "Current");
            company.SetLifecycle(CompanyLifecycleState.Seeding);
            company.SetLifecycle(CompanyLifecycleState.Registered);
            company.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
            registry.Companies.Add(company);
            await registry.SaveChangesAsync();
            return company.Id;
        });
        (finishedItemId, componentItemId) = await harness.InScopeAsync(async services =>
        {
            VumaRetailDbContext context = services.GetRequiredService<VumaRetailDbContext>();
            UnitOfMeasure each = UnitOfMeasure.CreateBase(harness.TenantId, "EA", "Each", UnitOfMeasureType.Count);
            each.AssignCompany(companyId);
            context.UnitsOfMeasure.Add(each);
            Item component = Item.Create(harness.TenantId, "COMP-API", "Component", ItemType.Stock, each.Id);
            component.AssignCompany(companyId);
            context.Items.Add(component);
            Item finished = Item.Create(harness.TenantId, "FIN-API", "Finished product", ItemType.Stock, each.Id);
            finished.AssignCompany(companyId);
            context.Items.Add(finished);
            await context.CommitAsync();
            return (finished.Id, component.Id);
        });
        Guid locationId = await harness.InScopeAsync(async services =>
        {
            VumaRetailDbContext context = services.GetRequiredService<VumaRetailDbContext>();
            StockLocation location = StockLocation.Create(harness.TenantId, harness.StoreId, "PROD", "Production", StockLocationType.Warehouse);
            location.AssignCompany(companyId);
            context.StockLocations.Add(location);
            await context.CommitAsync();
            IStockLedgerPoster poster = services.GetRequiredService<IStockLedgerPoster>();
            await poster.ReceiveAsync(location, componentItemId, null, new Quantity(1m, "EA"), new Money(5m, "ZAR"), note: null);
            await context.CommitAsync();
            StockBalance? balance = await context.StockBalances.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.LocationId == location.Id);
            balance?.AssignCompany(companyId);
            await context.CommitAsync();
            return location.Id;
        });

        HttpResponseMessage bomResponse = await client.PostAsJsonAsync(
            "/api/v1/manufacturing/boms/",
            new CreateBillOfMaterialsRequest(companyId, finishedItemId, null, 1, "Production BOM", [new BillOfMaterialsLineRequest(componentItemId, null, 1m, "EA")]));
        BillOfMaterialsIdResponse bom = (await bomResponse.Content.ReadFromJsonAsync<BillOfMaterialsIdResponse>())!;
        (await client.PostAsync($"/api/v1/manufacturing/boms/{bom.Id:D}/publish", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        Guid orderId = Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/v1/manufacturing/production-orders/", new CreateProductionOrderRequest(orderId, companyId, finishedItemId, 1m, "EA", "PROD-API-001", bom.Id))).StatusCode.Should().Be(HttpStatusCode.Created);
        Guid releaseOperationId = Guid.NewGuid();
        HttpResponseMessage released = await client.PostAsJsonAsync($"/api/v1/manufacturing/production-orders/{orderId:D}/release", new ReleaseProductionOrderRequest(releaseOperationId, bom.Id));
        string releaseBody = await released.Content.ReadAsStringAsync();
        released.StatusCode.Should().Be(HttpStatusCode.NoContent, releaseBody);
        ProductionOrderResponse releasedOrder = (await client.GetFromJsonAsync<ProductionOrderResponse>($"/api/v1/manufacturing/production-orders/{orderId:D}"))!;
        releasedOrder.Materials.Should().ContainSingle();
        releasedOrder.Materials[0].ComponentItemId.Should().Be(componentItemId);
        HttpResponseMessage issue = await client.PostAsJsonAsync($"/api/v1/manufacturing/production-orders/{orderId:D}/issues", new IssueProductionMaterialRequest(locationId, Guid.NewGuid(), releasedOrder.Materials[0].ComponentItemId, null, 1m, "EA", 5m, "ZAR"));
        string issueBody = await issue.Content.ReadAsStringAsync();
        issue.StatusCode.Should().Be(HttpStatusCode.NoContent, issueBody);
        (await client.PostAsJsonAsync($"/api/v1/manufacturing/production-orders/{orderId:D}/receipts", new ReceiveProductionOutputRequest(locationId, Guid.NewGuid(), 1m, "EA", 5m, "ZAR"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsync($"/api/v1/manufacturing/production-orders/{orderId:D}/close", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        ProductionOrderResponse order = (await client.GetFromJsonAsync<ProductionOrderResponse>($"/api/v1/manufacturing/production-orders/{orderId:D}"))!;
        order.Status.Should().Be("Closed");
        order.Issues.Should().ContainSingle();
        order.Receipts.Should().ContainSingle();
    }

    [Fact]
    public async Task Missing_bom_is_reported_as_a_domain_error()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("bom-viewer", "CorrectHorseBattery1", ManufacturingPermissions.View);
        using HttpClient client = await harness.SignInAsync("bom-viewer");

        HttpResponseMessage response = await client.GetAsync($"/api/v1/manufacturing/boms/{Guid.NewGuid():D}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private static void StubCompanyRouting(IServiceCollection services)
    {
        services.RemoveAll<ICompanyConnectionSecretStore>();
        services.AddSingleton<ICompanyConnectionSecretStore, TestSecretStore>();
        services.RemoveAll<ICompanyServingGuard>();
        services.AddSingleton<ICompanyServingGuard, OpenServingGuard>();
    }

    private sealed class TestSecretStore(IConfiguration configuration) : ICompanyConnectionSecretStore
    {
        public Task<string> ResolveAsync(string secretReference, CancellationToken cancellationToken = default)
            => Task.FromResult(configuration.GetConnectionString("Vuma")
                ?? throw new InvalidOperationException("The test host has no company database."));
    }

    private sealed class OpenServingGuard : ICompanyServingGuard
    {
        public Task EnsureServableAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsureAccessibleAsync(Guid tenantId, Guid companyId, CompanyAccessMode access, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Openapi_describes_the_BOM_routes()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        using JsonDocument document = JsonDocument.Parse(
            await harness.Client.GetStringAsync("/openapi/v1.json"));
        JsonElement paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/manufacturing/boms", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/boms/{id}", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/boms/{id}/publish", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Openapi_describes_the_production_execution_routes()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        using JsonDocument document = JsonDocument.Parse(await harness.Client.GetStringAsync("/openapi/v1.json"));
        JsonElement paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/manufacturing/production-orders", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/production-orders/{id}", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/production-orders/{id}/capacity", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/production-orders/{id}/release", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/production-orders/{id}/issues", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/production-orders/{id}/receipts", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/production-orders/{id}/scrap", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/production-orders/{id}/close", out _).Should().BeTrue();
    }
}
