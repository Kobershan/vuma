using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory.Commands;
using VumaRetail.Application.Inventory.Permissions;
using VumaRetail.Application.Registry;
using VumaRetail.Contracts.Inventory;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>
/// The Stage 08c endpoints over HTTP against the real store server: availability reads, the
/// reserve/release round-trip, the group permission gate, and OpenAPI presence.
/// </summary>
/// <remarks>
/// The host's company routing is stubbed at exactly two seams — the connection secret store and
/// the serving guard — so the factory opens the test database as the company's database. What is
/// under test is the endpoint wiring, the permission gates and the request shape; the reservation
/// mechanics themselves are proven at the service level, and the stubs are named as such rather
/// than hidden.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class AvailabilityApiTests
{
    private readonly PostgresFixture _fixture;

    /// <summary>Binds the shared PostgreSQL fixture.</summary>
    public AvailabilityApiTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Availability_and_reservations_round_trip_over_http()
    {
        await using var harness = await ApiHarness.CreateAsync(_fixture, configureServices: StubCompanyRouting).ConfigureAwait(false);

        Guid companyId = await SeedCompanyAsync(harness).ConfigureAwait(false);
        (Guid locationId, Guid itemId) = await SeedStockAsync(harness, companyId, 20m).ConfigureAwait(false);

        await harness.CreateUserAsync(
            "clerk1", "CorrectHorseBattery1",
            InventoryPermissions.AvailabilityView,
            InventoryPermissions.ReservationManage).ConfigureAwait(false);
        HttpClient clerk = await harness.SignInAsync("clerk1").ConfigureAwait(false);

        LocalAvailabilityResponse before = await GetLocalAsync(clerk, locationId, itemId).ConfigureAwait(false);
        before.Promise.Available.Should().Be(20m);
        before.Promise.AsAt.Should().BeAfter(DateTimeOffset.MinValue);

        var reserve = new ReserveStockRequest(
            locationId, itemId, null, 8m, "EA", "Order", Guid.NewGuid(),
            GroupDocumentRef: "SO-2026-000412",
            CompanyId: companyId);

        HttpResponseMessage reserved = await clerk.PostAsJsonAsync("/api/v1/inventory/reservations/reserve", reserve).ConfigureAwait(false);
        reserved.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await reserved.Content.ReadAsStringAsync().ConfigureAwait(false));

        var outcome = (await reserved.Content.ReadFromJsonAsync<ReserveStockResponse>())!;
        outcome.Held.Should().Be(8m);
        outcome.Shortfall.Should().Be(0m);
        outcome.ReservationId.Should().NotBeNull();

        LocalAvailabilityResponse after = await GetLocalAsync(clerk, locationId, itemId).ConfigureAwait(false);
        after.Promise.Available.Should().Be(12m);
        after.Promise.Reserved.Should().Be(8m);

        var release = new ReleaseReservationRequest(outcome.ReservationId!.Value, "Customer cancelled", companyId);
        HttpResponseMessage released = await clerk.PostAsJsonAsync("/api/v1/inventory/reservations/release", release).ConfigureAwait(false);
        released.StatusCode.Should().Be(HttpStatusCode.NoContent);

        LocalAvailabilityResponse restored = await GetLocalAsync(clerk, locationId, itemId).ConfigureAwait(false);
        restored.Promise.Available.Should().Be(20m);
    }

    [Fact]
    public async Task Group_availability_needs_the_registry_permission()
    {
        await using var harness = await ApiHarness.CreateAsync(_fixture, configureServices: StubCompanyRouting).ConfigureAwait(false);

        Guid companyId = await SeedCompanyAsync(harness).ConfigureAwait(false);
        (Guid locationId, Guid itemId) = await SeedStockAsync(harness, companyId, 20m).ConfigureAwait(false);

        await harness.CreateUserAsync(
            "clerk2", "CorrectHorseBattery1",
            InventoryPermissions.AvailabilityView,
            InventoryPermissions.ReservationManage).ConfigureAwait(false);
        HttpClient clerk = await harness.SignInAsync("clerk2").ConfigureAwait(false);

        await clerk.PostAsJsonAsync(
            "/api/v1/inventory/reservations/reserve",
            new ReserveStockRequest(locationId, itemId, null, 8m, "EA", "Order", Guid.NewGuid(), CompanyId: companyId))
            .ConfigureAwait(false);

        HttpResponseMessage refused = await clerk.GetAsync(
            $"/api/v1/availability?groupScope=true&itemId={itemId}").ConfigureAwait(false);
        refused.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            await refused.Content.ReadAsStringAsync().ConfigureAwait(false));

        await harness.CreateUserAsync(
            "planner1", "CorrectHorseBattery1",
            InventoryPermissions.AvailabilityView,
            RegistryPermissions.GroupAvailabilityView).ConfigureAwait(false);
        HttpClient planner = await harness.SignInAsync("planner1").ConfigureAwait(false);

        HttpResponseMessage allowed = await planner.GetAsync(
            $"/api/v1/availability?groupScope=true&itemId={itemId}").ConfigureAwait(false);
        allowed.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await allowed.Content.ReadAsStringAsync().ConfigureAwait(false));

        var view = (await allowed.Content.ReadFromJsonAsync<GroupAvailabilityResponse>())!;
        GroupAvailabilityContributionResponse contribution = view.Contributions.Should().ContainSingle().Subject;
        contribution.CompanyCode.Should().Be("SH");
        contribution.Promise.Available.Should().Be(12m);
        contribution.IsStale.Should().BeFalse();
        view.AsAt.Should().BeAfter(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task The_availability_and_reservation_routes_appear_in_openapi()
    {
        await using var harness = await ApiHarness.CreateAsync(_fixture).ConfigureAwait(false);

        string document = await harness.Client.GetStringAsync("/openapi/v1.json").ConfigureAwait(false);

        document.Should().Contain("/api/v1/availability");
        document.Should().Contain("/api/v1/inventory/reservations/reserve");
        document.Should().Contain("/api/v1/inventory/reservations/consume");
        document.Should().Contain("/api/v1/inventory/reservations/release");
    }

    private static async Task<LocalAvailabilityResponse> GetLocalAsync(HttpClient client, Guid locationId, Guid itemId)
    {
        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/availability?locationId={locationId}&itemId={itemId}").ConfigureAwait(false);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<LocalAvailabilityResponse>())!;
    }

    private static void StubCompanyRouting(IServiceCollection services)
    {
        services.RemoveAll<ICompanyConnectionSecretStore>();
        services.AddSingleton<ICompanyConnectionSecretStore, TestSecretStore>();
        services.RemoveAll<ICompanyServingGuard>();
        services.AddSingleton<ICompanyServingGuard, OpenServingGuard>();
    }

    private async Task<Guid> SeedCompanyAsync(ApiHarness harness)
    {
        return await harness.InScopeAsync(async provider =>
        {
            ITenantContext tenant = provider.GetRequiredService<ITenantContext>();
            var registry = provider.GetRequiredService<VumaRegistryDbContext>();

            Company company = Company.Create(tenant.TenantId, "SH", "Siyaya Hardware", "Siyaya Hardware", "ZAR", "en-ZA", "SH");
            company.SetConnectionSecretRef("test://company");
            company.SetMigration(1, "Current");
            company.SetLifecycle(CompanyLifecycleState.Seeding);
            company.SetLifecycle(CompanyLifecycleState.Registered);
            company.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
            registry.Companies.Add(company);
            await registry.SaveChangesAsync().ConfigureAwait(false);

            return company.Id;
        }).ConfigureAwait(false);
    }

    private async Task<(Guid LocationId, Guid ItemId)> SeedStockAsync(ApiHarness harness, Guid companyId, decimal onHand)
    {
        Guid locationId = await harness.SendAsync(new CreateStockLocationCommand("MAIN", "Back room", StockLocationType.Warehouse)).ConfigureAwait(false);

        Guid itemId = await harness.InScopeAsync(async provider =>
        {
            ITenantContext tenant = provider.GetRequiredService<ITenantContext>();
            VumaRetailDbContext context = provider.GetRequiredService<VumaRetailDbContext>();

            var each = UnitOfMeasure.CreateBase(tenant.TenantId, "EA", "Each", UnitOfMeasureType.Count);
            each.AssignCompany(companyId);
            context.UnitsOfMeasure.Add(each);
            await context.SaveChangesAsync().ConfigureAwait(false);

            var milk = Item.Create(tenant.TenantId, "MILK-2L", "Full cream milk 2L", ItemType.Stock, each.Id);
            milk.AssignCompany(companyId);
            context.Items.Add(milk);
            await context.SaveChangesAsync().ConfigureAwait(false);

            // A provisioned company database holds that company's rows stamped with its company:
            // under a bound company the global filter hides anything else, so the seed mirrors it.
            StockLocation? location = await context.StockLocations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(l => l.Id == locationId).ConfigureAwait(false);
            location?.AssignCompany(companyId);
            await context.SaveChangesAsync().ConfigureAwait(false);

            return milk.Id;
        }).ConfigureAwait(false);

        await harness.SendAsync(new ReceiveStockCommand(
            locationId, itemId, null,
            new Quantity(onHand, "EA"), new Money(10m, "ZAR"), "API seed")).ConfigureAwait(false);

        // The receipt above posts through the ambient (unbound) poster, so its balance row lands
        // company-less; stamp it like a provisioned database would hold it.
        await harness.InScopeAsync(async provider =>
        {
            VumaRetailDbContext context = provider.GetRequiredService<VumaRetailDbContext>();

            Domain.Inventory.StockBalance? balance = await context.StockBalances
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(b => b.LocationId == locationId).ConfigureAwait(false);
            balance?.AssignCompany(companyId);
            await context.SaveChangesAsync().ConfigureAwait(false);

            return 0;
        }).ConfigureAwait(false);

        return (locationId, itemId);
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
