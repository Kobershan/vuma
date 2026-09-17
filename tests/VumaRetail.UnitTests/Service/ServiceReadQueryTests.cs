using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Service;
using VumaRetail.Domain.Service;

namespace VumaRetail.UnitTests.Service;

public sealed class ServiceReadQueryTests
{
    [Fact]
    public async Task Warranty_reads_keep_only_the_active_tenant_and_company()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        WarrantyClaim visible = WarrantyClaim.Submit(tenantId, null, companyId, Guid.NewGuid(), Guid.NewGuid(), "SALE-1", new DateOnly(2026, 1, 1), "SERIAL-A", DateTimeOffset.UtcNow);
        WarrantyClaim otherTenant = WarrantyClaim.Submit(Guid.NewGuid(), null, companyId, Guid.NewGuid(), Guid.NewGuid(), "SALE-2", new DateOnly(2026, 1, 1), "SERIAL-B", DateTimeOffset.UtcNow);
        IServiceRepository repository = Substitute.For<IServiceRepository>();
        repository.ListWarrantiesAsync(companyId, null, Arg.Any<CancellationToken>()).Returns([visible, otherTenant]);
        ICompanyContext company = Substitute.For<ICompanyContext>(); company.CompanyId.Returns(companyId);
        ITenantContext tenant = Substitute.For<ITenantContext>(); tenant.TenantId.Returns(tenantId);

        IReadOnlyList<ServiceWarrantyResult> result = await new ListServiceWarrantiesQueryHandler(repository, company, tenant)
            .HandleAsync(new ListServiceWarrantiesQuery(companyId));

        result.Should().ContainSingle().Which.Id.Should().Be(visible.Id);
    }

    [Fact]
    public async Task Repair_reads_require_the_active_company()
    {
        Guid companyId = Guid.NewGuid();
        IServiceRepository repository = Substitute.For<IServiceRepository>();
        ICompanyContext company = Substitute.For<ICompanyContext>(); company.CompanyId.Returns(Guid.NewGuid());

        Func<Task> action = () => new ListServiceRepairsQueryHandler(repository, company, Substitute.For<ITenantContext>())
            .HandleAsync(new ListServiceRepairsQuery(companyId));

        await action.Should().ThrowAsync<InvalidOperationException>();
        await repository.DidNotReceive().ListRepairsAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Part_usage_reads_preserve_currency_and_quantity()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        ServicePartUsage usage = ServicePartUsage.Issue(tenantId, null, companyId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 2m, 30m, "ZAR", DateTimeOffset.UtcNow);
        IServiceRepository repository = Substitute.For<IServiceRepository>();
        repository.ListPartUsagesAsync(companyId, null, Arg.Any<CancellationToken>()).Returns([usage]);
        ICompanyContext company = Substitute.For<ICompanyContext>(); company.CompanyId.Returns(companyId);
        ITenantContext tenant = Substitute.For<ITenantContext>(); tenant.TenantId.Returns(tenantId);

        ServicePartUsageResult result = (await new ListServicePartUsagesQueryHandler(repository, company, tenant)
            .HandleAsync(new ListServicePartUsagesQuery(companyId))).Single();

        result.Quantity.Should().Be(2m);
        result.UnitCost.Should().Be(30m);
        result.Currency.Should().Be("ZAR");
    }
}
