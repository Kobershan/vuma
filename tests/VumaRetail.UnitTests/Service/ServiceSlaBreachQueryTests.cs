#pragma warning disable CS1591
using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Service;
using VumaRetail.Domain.Service;

namespace VumaRetail.UnitTests.Service;

public sealed class ServiceSlaBreachQueryTests
{
    [Fact]
    public async Task Breach_query_returns_only_the_active_tenant_and_company_rows()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        Guid ticketId = Guid.NewGuid();
        DateTimeOffset due = DateTimeOffset.UtcNow.AddHours(-2);
        ServiceSlaBreachEvent visible = ServiceSlaBreachEvent.Record(tenantId, null, companyId, ticketId,
            "standard", ServiceSlaBreachType.Response, due, due.AddMinutes(1));
        ServiceSlaBreachEvent otherTenant = ServiceSlaBreachEvent.Record(Guid.NewGuid(), null, companyId,
            Guid.NewGuid(), "standard", ServiceSlaBreachType.Response, due, due.AddMinutes(1));

        IServiceRepository services = Substitute.For<IServiceRepository>();
        services.ListSlaBreachesAsync(companyId, null, Arg.Any<CancellationToken>())
            .Returns([visible, otherTenant]);
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);

        IReadOnlyList<ServiceSlaBreachResult> result = await new ListServiceSlaBreachesQueryHandler(
            services, company, tenant).HandleAsync(new ListServiceSlaBreachesQuery(companyId));

        result.Should().ContainSingle().Which.Id.Should().Be(visible.Id);
    }
}
