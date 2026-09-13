using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Service;
using VumaRetail.Domain.Service;

namespace VumaRetail.UnitTests.Service;

public sealed class ServiceCommandTests
{
    [Fact]
    public async Task Opening_a_service_ticket_replays_the_same_operation_without_a_second_record()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        IServiceRepository repository = Substitute.For<IServiceRepository>();
        repository.FindTicketByOperationIdAsync(operationId, Arg.Any<CancellationToken>()).Returns((ServiceTicket?)null);
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var handler = new OpenServiceTicketCommandHandler(repository, tenant, company, clock);
        var command = new OpenServiceTicketCommand(operationId, companyId, Guid.NewGuid(), "Laptop repair");

        Guid id = await handler.HandleAsync(command);
        repository.Received(1).Add(Arg.Any<ServiceTicket>());
        id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_ticket_from_another_company_cannot_be_approved()
    {
        Guid companyId = Guid.NewGuid();
        WarrantyClaim claim = WarrantyClaim.Submit(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "SALE-1", new DateOnly(2026, 1, 2), "SERIAL-A", DateTimeOffset.UtcNow);
        IServiceRepository repository = Substitute.For<IServiceRepository>();
        repository.FindWarrantyAsync(claim.Id, Arg.Any<CancellationToken>()).Returns(claim);
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        Func<Task> action = () => new ApproveWarrantyClaimCommandHandler(repository, company, clock)
            .HandleAsync(new ApproveWarrantyClaimCommand(claim.Id, "SERIAL-A"));

        await action.Should().ThrowAsync<InvalidOperationException>();
        claim.Status.Should().Be(WarrantyClaimStatus.Pending);
    }
}
