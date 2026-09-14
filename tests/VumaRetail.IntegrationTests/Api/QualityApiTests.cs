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
using VumaRetail.Application.Inventory.Commands;
using VumaRetail.Application.Inventory.Permissions;
using VumaRetail.Application.Quality;
using VumaRetail.Application.Registry;
using VumaRetail.Contracts.Inventory;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Quality;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>Stage 18 quality route and permission-boundary evidence.</summary>
[Collection(PostgresCollection.Name)]
public sealed class QualityApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Quality_openapi_contains_plans_certificates_and_recalls()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        using JsonDocument document = JsonDocument.Parse(await harness.Client.GetStringAsync("/openapi/v1.json"));
        JsonElement paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/quality/inspection-plans", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/quality/inspection-plans/{id}/publish", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/quality/certificates", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/quality/certificates/{id}/revoke", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/quality/recalls", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/quality/recalls/{id}/trace", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/quality/recalls/{id}/close", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Quality_viewer_cannot_mutate_plans_certificates_or_recalls()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("quality-viewer", "CorrectHorseBattery1", QualityPermissions.View);
        using HttpClient client = await harness.SignInAsync("quality-viewer");

        HttpResponseMessage plan = await client.PostAsJsonAsync("/api/v1/quality/inspection-plans", new
        {
            CompanyId = Guid.NewGuid(), ItemId = Guid.NewGuid(), ItemVariantId = (Guid?)null,
            Version = 1, Name = "Incoming", SampleSize = 1, AcceptanceCriteria = "Pass"
        });
        HttpResponseMessage certificate = await client.PostAsJsonAsync("/api/v1/quality/certificates", new
        {
            CompanyId = Guid.NewGuid(), ItemId = Guid.NewGuid(), ItemVariantId = (Guid?)null,
            CertificateNumber = "CERT-DENIED", Issuer = "Lab", IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), Evidence = "hash"
        });
        HttpResponseMessage recall = await client.PostAsJsonAsync("/api/v1/quality/recalls", new
        {
            OperationId = Guid.NewGuid(), CompanyId = Guid.NewGuid(), CaseNumber = "REC-DENIED",
            LotReference = "LOT-1", Reason = "test"
        });

        plan.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        certificate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        recall.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authorized_quality_request_binds_the_company_for_creation()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("quality-manager", "CorrectHorseBattery1", QualityPermissions.Manage, QualityPermissions.View);
        using HttpClient client = await harness.SignInAsync("quality-manager");

        Guid companyId = Guid.NewGuid();
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/quality/inspection-plans", new
        {
            CompanyId = companyId, ItemId = Guid.NewGuid(), ItemVariantId = (Guid?)null,
            Version = 1, Name = "Incoming goods", SampleSize = 3, AcceptanceCriteria = "No visible damage"
        });

        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
    }

    [Fact]
    public async Task Quality_hold_reduces_availability_is_idempotent_and_refuses_shortfall()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture, configureServices: StubCompanyRouting);
        Guid companyId = await SeedCompanyAsync(harness);
        (Guid locationId, Guid itemId) = await SeedStockAsync(harness, companyId, 100m);
        await harness.CreateUserAsync(
            "quality-hold-manager", "CorrectHorseBattery1",
            QualityPermissions.Manage, QualityPermissions.View,
            InventoryPermissions.AvailabilityView);
        using HttpClient client = await harness.SignInAsync("quality-hold-manager");

        Guid operationId = Guid.NewGuid();
        var request = new
        {
            OperationId = operationId, CompanyId = companyId, LocationId = locationId,
            ItemId = itemId, ItemVariantId = (Guid?)null, Quantity = 20m,
            UnitOfMeasure = "EA", Reason = "Incoming inspection"
        };

        HttpResponseMessage first = await client.PostAsJsonAsync("/api/v1/quality/holds/", request);
        string firstBody = await first.Content.ReadAsStringAsync();
        first.StatusCode.Should().Be(HttpStatusCode.Created, firstBody);
        Guid holdId = (await first.Content.ReadFromJsonAsync<Guid>())!;
        holdId.Should().NotBeEmpty();

        HttpResponseMessage retry = await client.PostAsJsonAsync("/api/v1/quality/holds/", request);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        (await retry.Content.ReadFromJsonAsync<Guid>()).Should().Be(holdId);

        LocalAvailabilityResponse held = await GetAvailabilityAsync(client, locationId, itemId);
        held.Promise.OnHand.Should().Be(100m);
        held.Promise.Reserved.Should().Be(20m);
        held.Promise.Available.Should().Be(80m);

        HttpResponseMessage shortfall = await client.PostAsJsonAsync("/api/v1/quality/holds/", new
        {
            OperationId = Guid.NewGuid(), CompanyId = companyId, LocationId = locationId,
            ItemId = itemId, ItemVariantId = (Guid?)null, Quantity = 81m,
            UnitOfMeasure = "EA", Reason = "Second inspection"
        });
        shortfall.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        LocalAvailabilityResponse unchanged = await GetAvailabilityAsync(client, locationId, itemId);
        unchanged.Promise.Reserved.Should().Be(20m);
        unchanged.Promise.Available.Should().Be(80m);
    }

    [Fact]
    public async Task Opening_recall_through_api_traces_production_output_and_shipment_in_postgres()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture, configureServices: StubCompanyRouting);
        Guid companyId = await SeedCompanyAsync(harness);
        Guid productionId = Guid.NewGuid();
        Guid shipmentId = Guid.NewGuid();
        Guid locationId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();
        const string inputLot = "LOT-INPUT-POSTGRES";
        const string outputLot = "LOT-OUTPUT-POSTGRES";

        await harness.InScopeAsync(async services =>
        {
            ITenantContext tenant = services.GetRequiredService<ITenantContext>();
            VumaRetailDbContext context = services.GetRequiredService<VumaRetailDbContext>();
            StockLedgerEntry[] movements =
            [
                StockLedgerEntry.Post(tenant.TenantId, null, locationId, null, itemId, null,
                    StockMovementType.ProductionIssue, new Quantity(-2m, "EA"), new Money(10m, "ZAR"),
                    StockReferenceType.Production, productionId, null, "production input", inputLot),
                StockLedgerEntry.Post(tenant.TenantId, null, locationId, null, itemId, null,
                    StockMovementType.ProductionReceipt, new Quantity(2m, "EA"), new Money(10m, "ZAR"),
                    StockReferenceType.Production, productionId, null, "production output", outputLot),
                StockLedgerEntry.Post(tenant.TenantId, null, locationId, null, itemId, null,
                    StockMovementType.SaleIssue, new Quantity(-1m, "EA"), new Money(10m, "ZAR"),
                    StockReferenceType.Shipment, shipmentId, null, "shipment", outputLot),
            ];
            foreach (StockLedgerEntry movement in movements)
            {
                movement.AssignCompany(companyId);
            }
            context.StockLedgerEntries.AddRange(movements);
            await context.SaveChangesAsync();
        });

        await harness.CreateUserAsync("quality-recall-manager", "CorrectHorseBattery1", QualityPermissions.Manage, QualityPermissions.View);
        using HttpClient client = await harness.SignInAsync("quality-recall-manager");
        Guid operationId = Guid.NewGuid();
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/quality/recalls/", new
        {
            OperationId = operationId, CompanyId = companyId, CaseNumber = "REC-POSTGRES-GENEALOGY",
            LotReference = inputLot, Reason = "Contamination"
        });
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        Guid recallId = (await response.Content.ReadFromJsonAsync<Guid>())!;

        await harness.InScopeAsync(async services =>
        {
            VumaRetailDbContext context = services.GetRequiredService<VumaRetailDbContext>();
            RecallCase recall = (await context.RecallCases.SingleAsync(value => value.Id == recallId));
            recall.TraceReferences.Should().Contain(reference =>
                reference.Kind == StockReferenceType.Production.ToString() && reference.Reference == productionId.ToString());
            recall.TraceReferences.Should().Contain(reference =>
                reference.Kind == StockReferenceType.Shipment.ToString() && reference.Reference == shipmentId.ToString());
        });
    }

    private static async Task<LocalAvailabilityResponse> GetAvailabilityAsync(HttpClient client, Guid locationId, Guid itemId)
    {
        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/availability?locationId={locationId}&itemId={itemId}");
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return (await response.Content.ReadFromJsonAsync<LocalAvailabilityResponse>())!;
    }

    private static async Task<Guid> SeedCompanyAsync(ApiHarness harness)
    {
        return await harness.InScopeAsync(async services =>
        {
            ITenantContext tenant = services.GetRequiredService<ITenantContext>();
            VumaRegistryDbContext registry = services.GetRequiredService<VumaRegistryDbContext>();
            Company company = Company.Create(tenant.TenantId, "QH", "Quality Holdings", "Quality Holdings", "ZAR", "en-ZA", "QH");
            company.SetConnectionSecretRef("test://company");
            company.SetMigration(1, "Current");
            company.SetLifecycle(CompanyLifecycleState.Seeding);
            company.SetLifecycle(CompanyLifecycleState.Registered);
            company.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
            registry.Companies.Add(company);
            await registry.SaveChangesAsync();
            return company.Id;
        });
    }

    private static async Task<(Guid LocationId, Guid ItemId)> SeedStockAsync(ApiHarness harness, Guid companyId, decimal quantity)
    {
        Guid locationId = await harness.SendAsync(new CreateStockLocationCommand("QUALITY", "Quality stock", StockLocationType.Warehouse));
        Guid itemId = await harness.InScopeAsync(async services =>
        {
            ITenantContext tenant = services.GetRequiredService<ITenantContext>();
            VumaRetailDbContext context = services.GetRequiredService<VumaRetailDbContext>();
            UnitOfMeasure each = UnitOfMeasure.CreateBase(tenant.TenantId, "EA", "Each", UnitOfMeasureType.Count);
            each.AssignCompany(companyId);
            context.UnitsOfMeasure.Add(each);
            Item item = Item.Create(tenant.TenantId, "QUALITY-STOCK", "Quality stock", ItemType.Stock, each.Id);
            item.AssignCompany(companyId);
            context.Items.Add(item);
            await context.SaveChangesAsync();
            StockLocation location = (await context.StockLocations.IgnoreQueryFilters()
                .SingleAsync(value => value.Id == locationId));
            location.AssignCompany(companyId);
            await context.SaveChangesAsync();
            return item.Id;
        });

        await harness.SendAsync(new ReceiveStockCommand(
            locationId, itemId, null, new Quantity(quantity, "EA"), new Money(10m, "ZAR"), "Quality seed"));
        await harness.InScopeAsync(async services =>
        {
            VumaRetailDbContext context = services.GetRequiredService<VumaRetailDbContext>();
            StockBalance balance = (await context.StockBalances.IgnoreQueryFilters()
                .SingleAsync(value => value.LocationId == locationId));
            balance.AssignCompany(companyId);
            await context.SaveChangesAsync();
            return 0;
        });
        return (locationId, itemId);
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
        public Task EnsureServableAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task EnsureAccessibleAsync(Guid tenantId, Guid companyId, CompanyAccessMode access, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
