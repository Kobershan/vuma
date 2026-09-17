using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Service;
using VumaRetail.Domain.Service;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.IntegrationTests.Api;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Service;

[Collection(PostgresCollection.Name)]
public sealed class ServiceCustodyIsolationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Custody_reads_return_only_the_active_tenant_and_company()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        Guid companyId = Guid.NewGuid();
        Guid otherCompanyId = Guid.NewGuid();
        Guid otherTenantId = Guid.NewGuid();

        await harness.InScopeAsync<object?>(async services =>
        {
            VumaRetailDbContext db = services.GetRequiredService<VumaRetailDbContext>();
            db.ServiceCustodyEvents.Add(ServiceCustodyEvent.Record(
                harness.TenantId, null, companyId, Guid.NewGuid(), Guid.NewGuid(), "received", "owned-item", harness.Clock.UtcNow));
            db.ServiceCustodyEvents.Add(ServiceCustodyEvent.Record(
                harness.TenantId, null, otherCompanyId, Guid.NewGuid(), Guid.NewGuid(), "received", "other-company-item", harness.Clock.UtcNow));
            db.ServiceCustodyEvents.Add(ServiceCustodyEvent.Record(
                otherTenantId, null, companyId, Guid.NewGuid(), Guid.NewGuid(), "received", "other-tenant-item", harness.Clock.UtcNow));
            await db.CommitAsync();
            return null;
        });

        await harness.InScopeAsync<object?>(async services =>
        {
            services.GetRequiredService<ICompanyContext>().SetCompany(companyId);
            IReadOnlyList<ServiceCustodyEvent> rows = await services
                .GetRequiredService<IServiceRepository>()
                .ListCustodyAsync(companyId);

            rows.Should().ContainSingle();
            rows[0].ItemReference.Should().Be("owned-item");
            return null;
        });
    }
}
