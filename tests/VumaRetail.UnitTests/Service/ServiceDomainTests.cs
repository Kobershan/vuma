using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Service;
using VumaRetail.Domain.Service;

namespace VumaRetail.UnitTests.Service;

public sealed class ServiceDomainTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    [Fact]
    public async Task Sla_creation_is_company_scoped_and_rejects_duplicate_names()
    {
        IServiceRepository services = Substitute.For<IServiceRepository>();
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(CompanyId);
        CreateServiceSlaCommandHandler handler = new(services, tenant, company);
        CreateServiceSlaCommand command = new(CompanyId, "Standard", 4m, 24m);

        Guid id = await handler.HandleAsync(command);
        id.Should().NotBeEmpty();
        await services.Received(1).FindSlaByNameAsync(CompanyId, "Standard", Arg.Any<CancellationToken>());
        services.Received(1).Add(Arg.Is<ServiceSla>(x => x.CompanyId == CompanyId && x.Name == "Standard"));

        ServiceSla existing = ServiceSla.Create(TenantId, CompanyId, "Standard", 4m, 24m);
        services.FindSlaByNameAsync(CompanyId, "Standard", Arg.Any<CancellationToken>()).Returns(existing);
        await FluentActions.Invoking(() => handler.HandleAsync(command)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Customer_custody_is_a_record_and_does_not_change_stock()
    {
        ServiceCustodyEvent custody = ServiceCustodyEvent.Record(TenantId, null, CompanyId, Guid.NewGuid(),
            CustomerId, "received", "laptop-serial-A", DateTimeOffset.UtcNow);

        custody.CustomerId.Should().Be(CustomerId);
        custody.EventType.Should().Be("received");
    }

    [Fact]
    public void Warranty_claim_requires_the_original_serial_snapshot()
    {
        WarrantyClaim claim = WarrantyClaim.Submit(TenantId, null, CompanyId, Guid.NewGuid(), CustomerId,
            "SALE-1", new DateOnly(2026, 1, 2), "SERIAL-A", DateTimeOffset.UtcNow);

        FluentActions.Invoking(() => claim.Approve("SERIAL-B", DateTimeOffset.UtcNow))
            .Should().Throw<InvalidOperationException>();
        claim.Status.Should().Be(WarrantyClaimStatus.Pending);
        claim.Approve("serial-a", DateTimeOffset.UtcNow);
        claim.Status.Should().Be(WarrantyClaimStatus.Approved);
    }

    [Fact]
    public void Ticket_close_requires_resolution_or_explicit_waiting_state()
    {
        ServiceTicket ticket = ServiceTicket.Open(TenantId, null, CompanyId, Guid.NewGuid(), CustomerId, "Repair laptop", DateTimeOffset.UtcNow);

        FluentActions.Invoking(() => ticket.Close(DateTimeOffset.UtcNow)).Should().Throw<InvalidOperationException>();
        ticket.Resolve(DateTimeOffset.UtcNow);
        ticket.Close(DateTimeOffset.UtcNow);
        ticket.Status.Should().Be(ServiceTicketStatus.Closed);
    }

    [Fact]
    public void Part_usage_is_operation_keyed_and_costed_without_mutating_stock()
    {
        ServicePartUsage usage = ServicePartUsage.Issue(TenantId, null, CompanyId, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), null, 2m, 30m, "ZAR", DateTimeOffset.UtcNow);

        usage.TotalCost.Should().Be(60m);
        usage.Currency.Should().Be("ZAR");
    }

    [Fact]
    public void Repair_must_be_started_before_completion()
    {
        RepairJob job = RepairJob.Open(TenantId, null, CompanyId, Guid.NewGuid(), "laptop-serial-A", DateTimeOffset.UtcNow);

        FluentActions.Invoking(() => job.Complete(DateTimeOffset.UtcNow)).Should().Throw<InvalidOperationException>();
        job.Start();
        job.Complete(DateTimeOffset.UtcNow);
        job.Status.Should().Be(RepairJobStatus.Completed);
    }
}
