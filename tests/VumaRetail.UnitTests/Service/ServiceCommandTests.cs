using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;
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
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(claim.TenantId);
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        Func<Task> action = () => new ApproveWarrantyClaimCommandHandler(repository, tenant, company, clock)
            .HandleAsync(new ApproveWarrantyClaimCommand(claim.Id, "SERIAL-A"));

        await action.Should().ThrowAsync<InvalidOperationException>();
        claim.Status.Should().Be(WarrantyClaimStatus.Pending);
    }

    [Fact]
    public async Task Service_part_replay_returns_original_usage_without_issuing_again()
    {
        Guid operationId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        Guid repairId = Guid.NewGuid();
        ServicePartUsage usage = ServicePartUsage.Issue(Guid.NewGuid(), null, companyId, repairId, operationId,
            Guid.NewGuid(), null, 2m, 30m, "ZAR", DateTimeOffset.UtcNow);
        IServiceRepository services = Substitute.For<IServiceRepository>();
        services.FindPartUsageByOperationIdAsync(operationId, Arg.Any<CancellationToken>()).Returns(usage);
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        var handler = new IssueServicePartCommandHandler(services, Substitute.For<IStockLocationRepository>(),
            Substitute.For<IReservationService>(), Substitute.For<IStockLedgerPoster>(), company,
            Substitute.For<ITenantContext>(), Substitute.For<IClock>());
        Guid result = await handler.HandleAsync(new IssueServicePartCommand(operationId, companyId, repairId,
            Guid.NewGuid(), usage.ItemId, null, 2m, "EA"));
        result.Should().Be(usage.Id);
        services.DidNotReceive().Add(Arg.Any<ServicePartUsage>());
    }

    [Fact]
    public async Task Service_ticket_replay_requires_the_company_to_be_active_before_returning_existing_ticket()
    {
        Guid companyId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        Guid customerId = Guid.NewGuid();
        ServiceTicket existing = ServiceTicket.Open(Guid.NewGuid(), null, companyId, operationId, customerId,
            "Laptop repair", DateTimeOffset.UtcNow);
        IServiceRepository repository = Substitute.For<IServiceRepository>();
        repository.FindTicketByOperationIdAsync(operationId, Arg.Any<CancellationToken>()).Returns(existing);
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(Guid.NewGuid());

        Func<Task> action = () => new OpenServiceTicketCommandHandler(repository, Substitute.For<ITenantContext>(), company,
            Substitute.For<IClock>()).HandleAsync(new OpenServiceTicketCommand(operationId, companyId, customerId, "Laptop repair"));

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Warranty_submission_rejects_a_ticket_from_another_tenant_or_customer()
    {
        Guid companyId = Guid.NewGuid();
        Guid customerId = Guid.NewGuid();
        ServiceTicket ticket = ServiceTicket.Open(Guid.NewGuid(), null, companyId, Guid.NewGuid(), Guid.NewGuid(),
            "Laptop repair", DateTimeOffset.UtcNow);
        IServiceRepository repository = Substitute.For<IServiceRepository>();
        repository.FindTicketAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());

        Func<Task> action = () => new SubmitWarrantyClaimCommandHandler(repository, tenant, company,
            Substitute.For<IClock>()).HandleAsync(new SubmitWarrantyClaimCommand(companyId, ticket.Id, customerId,
                "SALE-1", new DateOnly(2026, 1, 2), "SERIAL-A"));

        await action.Should().ThrowAsync<InvalidOperationException>();
        repository.DidNotReceive().Add(Arg.Any<WarrantyClaim>());
    }

    [Fact]
    public async Task Repair_opening_rejects_a_ticket_from_another_company()
    {
        Guid companyId = Guid.NewGuid();
        ServiceTicket ticket = ServiceTicket.Open(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Laptop repair", DateTimeOffset.UtcNow);
        IServiceRepository repository = Substitute.For<IServiceRepository>();
        repository.FindTicketAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(ticket.TenantId);

        Func<Task> action = () => new OpenRepairJobCommandHandler(repository, tenant, company,
            Substitute.For<IClock>()).HandleAsync(new OpenRepairJobCommand(companyId, ticket.Id, "SERIAL-A"));

        await action.Should().ThrowAsync<InvalidOperationException>();
        repository.DidNotReceive().Add(Arg.Any<RepairJob>());
    }

    [Fact]
    public async Task Service_part_issue_rejects_a_repair_from_another_tenant_or_company()
    {
        Guid companyId = Guid.NewGuid();
        RepairJob repair = RepairJob.Open(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "SERIAL-A", DateTimeOffset.UtcNow);
        IServiceRepository repository = Substitute.For<IServiceRepository>();
        repository.FindPartUsageByOperationIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ServicePartUsage?)null);
        repository.FindRepairAsync(repair.Id, Arg.Any<CancellationToken>()).Returns(repair);
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());

        Func<Task> action = () => new IssueServicePartCommandHandler(repository,
            Substitute.For<IStockLocationRepository>(), Substitute.For<IReservationService>(),
            Substitute.For<IStockLedgerPoster>(), company, tenant, Substitute.For<IClock>()).HandleAsync(
                new IssueServicePartCommand(Guid.NewGuid(), companyId, repair.Id, Guid.NewGuid(), Guid.NewGuid(), null, 1m, "EA"));

        await action.Should().ThrowAsync<InvalidOperationException>();
        repository.DidNotReceive().Add(Arg.Any<ServicePartUsage>());
    }
}
