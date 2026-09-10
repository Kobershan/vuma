using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Contracts.Registry;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Web.Registry;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>Stage 06c company lifecycle read and permission-boundary API coverage.</summary>
[Collection(PostgresCollection.Name)]
public sealed class CompaniesApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Company_viewer_can_list_active_companies_and_read_migration_status()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        Guid companyId = await SeedActiveCompanyAsync(harness);
        await harness.CreateUserAsync("company-viewer", "CorrectHorseBattery1", PlatformPermissions.CompanyView);
        using HttpClient client = await harness.SignInAsync("company-viewer");

        IReadOnlyList<CompanyResponse> companies = (await client.GetFromJsonAsync<IReadOnlyList<CompanyResponse>>(
            "/api/v1/companies/")) ?? throw new InvalidOperationException("Missing companies response.");
        companies.Should().ContainSingle(company => company.Id == companyId && company.IsActive);

        CompanyMigrationStatusResponse status = (await client.GetFromJsonAsync<CompanyMigrationStatusResponse>(
            $"/api/v1/companies/{companyId:D}/migration")) ?? throw new InvalidOperationException("Missing migration status.");
        status.CompanyId.Should().Be(companyId);
        status.MigrationState.Should().Be("Current");
        status.PendingAction.Should().BeNull();
    }

    [Fact]
    public async Task Company_viewer_cannot_provision_or_deactivate_a_company()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        Guid companyId = await SeedActiveCompanyAsync(harness);
        await harness.CreateUserAsync("company-reader", "CorrectHorseBattery1", PlatformPermissions.CompanyView);
        using HttpClient client = await harness.SignInAsync("company-reader");

        HttpResponseMessage provision = await client.PostAsJsonAsync("/api/v1/companies/",
            new ProvisionCompanyRequest("DENY", "Denied Company", "Denied Company", "ZAR", "en-ZA", "DN"));
        provision.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        HttpResponseMessage deactivate = await client.PostAsJsonAsync(
            $"/api/v1/companies/{companyId:D}/deactivate",
            new CompanyEndpoints.DeactivateCompanyRequest("permission test"));
        deactivate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static Task<Guid> SeedActiveCompanyAsync(ApiHarness harness)
        => harness.InScopeAsync(async provider =>
        {
            ITenantContext tenant = provider.GetRequiredService<ITenantContext>();
            VumaRegistryDbContext registry = provider.GetRequiredService<VumaRegistryDbContext>();
            Company company = Company.Create(tenant.TenantId, "API", "API Company", "API Company", "ZAR", "en-ZA", "API");
            company.SetConnectionSecretRef("test://company");
            company.SetMigration(1, "Current");
            company.SetLifecycle(CompanyLifecycleState.Seeding);
            company.SetLifecycle(CompanyLifecycleState.Registered);
            company.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
            registry.Companies.Add(company);
            await registry.SaveChangesAsync().ConfigureAwait(false);
            return company.Id;
        });
}
