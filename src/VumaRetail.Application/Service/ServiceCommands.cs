#pragma warning disable CS1591, IDE0011
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Service;

namespace VumaRetail.Application.Service;

[CommandSideEffect(SideEffect.Write)]
public sealed record OpenServiceTicketCommand(Guid OperationId, Guid CompanyId, Guid CustomerId, string Subject) : ICommand<Guid>;

public sealed class OpenServiceTicketCommandHandler(IServiceRepository services, ITenantContext tenant,
    ICompanyContext company, IClock clock) : ICommandHandler<OpenServiceTicketCommand, Guid>
{
    public async Task<Guid> HandleAsync(OpenServiceTicketCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ServiceTicket? existing = await services.FindTicketByOperationIdAsync(command.OperationId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.CompanyId != command.CompanyId || existing.CustomerId != command.CustomerId ||
                !string.Equals(existing.Subject, command.Subject.Trim(), StringComparison.Ordinal))
                throw new InvalidOperationException("The service ticket operation was replayed with different content.");
            return existing.Id;
        }
        EnsureCompany(company, command.CompanyId);
        ServiceTicket ticket = ServiceTicket.Open(tenant.TenantId, null, command.CompanyId, command.OperationId, command.CustomerId,
            command.Subject, clock.UtcNow);
        services.Add(ticket);
        return ticket.Id;
    }

    private static void EnsureCompany(ICompanyContext company, Guid expected)
    {
        if (company.CompanyId is not { } active || active != expected)
            throw new InvalidOperationException("The service company is not the active company.");
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record ApproveWarrantyClaimCommand(Guid ClaimId, string SoldSerialNumber) : ICommand;

[CommandSideEffect(SideEffect.Write)]
public sealed record SubmitWarrantyClaimCommand(Guid CompanyId, Guid TicketId, Guid CustomerId, string SaleReference,
    DateOnly SaleDate, string SerialNumber) : ICommand<Guid>;

public sealed class SubmitWarrantyClaimCommandHandler(IServiceRepository services, ITenantContext tenant,
    ICompanyContext company, IClock clock) : ICommandHandler<SubmitWarrantyClaimCommand, Guid>
{
    public Task<Guid> HandleAsync(SubmitWarrantyClaimCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (company.CompanyId is not { } active || active != command.CompanyId)
            throw new InvalidOperationException("The warranty company is not the active company.");
        WarrantyClaim claim = WarrantyClaim.Submit(tenant.TenantId, null, command.CompanyId, command.TicketId,
            command.CustomerId, command.SaleReference, command.SaleDate, command.SerialNumber, clock.UtcNow);
        services.Add(claim);
        return Task.FromResult(claim.Id);
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record OpenRepairJobCommand(Guid CompanyId, Guid TicketId, string ItemReference) : ICommand<Guid>;

public sealed class OpenRepairJobCommandHandler(IServiceRepository services, ITenantContext tenant,
    ICompanyContext company, IClock clock) : ICommandHandler<OpenRepairJobCommand, Guid>
{
    public Task<Guid> HandleAsync(OpenRepairJobCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (company.CompanyId is not { } active || active != command.CompanyId)
            throw new InvalidOperationException("The repair company is not the active company.");
        RepairJob job = RepairJob.Open(tenant.TenantId, null, command.CompanyId, command.TicketId, command.ItemReference, clock.UtcNow);
        services.Add(job);
        return Task.FromResult(job.Id);
    }
}

public sealed class ApproveWarrantyClaimCommandHandler(IServiceRepository services, ICompanyContext company, IClock clock)
    : ICommandHandler<ApproveWarrantyClaimCommand, Unit>
{
    public async Task<Unit> HandleAsync(ApproveWarrantyClaimCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WarrantyClaim claim = await services.FindWarrantyAsync(command.ClaimId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Warranty claim not found.");
        if (company.CompanyId is not { } active || claim.CompanyId != active)
            throw new InvalidOperationException("The warranty company is not the active company.");
        claim.Approve(command.SoldSerialNumber, clock.UtcNow);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record CompleteRepairCommand(Guid RepairJobId) : ICommand;

public sealed class CompleteRepairCommandHandler(IServiceRepository services, ICompanyContext company, IClock clock)
    : ICommandHandler<CompleteRepairCommand, Unit>
{
    public async Task<Unit> HandleAsync(CompleteRepairCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RepairJob job = await services.FindRepairAsync(command.RepairJobId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Repair job not found.");
        if (company.CompanyId is not { } active || job.CompanyId != active)
            throw new InvalidOperationException("The repair company is not the active company.");
        job.Complete(clock.UtcNow);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record CloseServiceTicketCommand(Guid TicketId) : ICommand;

public sealed class CloseServiceTicketCommandHandler(IServiceRepository services, ICompanyContext company, IClock clock)
    : ICommandHandler<CloseServiceTicketCommand, Unit>
{
    public async Task<Unit> HandleAsync(CloseServiceTicketCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ServiceTicket ticket = await services.FindTicketAsync(command.TicketId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Service ticket not found.");
        if (company.CompanyId is not { } active || ticket.CompanyId != active)
            throw new InvalidOperationException("The service company is not the active company.");
        ticket.Close(clock.UtcNow);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record IssueServicePartCommand(Guid OperationId, Guid CompanyId, Guid RepairJobId, Guid LocationId,
    Guid? ItemId, Guid? ItemVariantId, decimal Quantity, string UnitOfMeasure) : ICommand<Guid>;

public sealed class IssueServicePartCommandHandler : ICommandHandler<IssueServicePartCommand, Guid>
{
    private readonly IServiceRepository services;
    private readonly IStockLocationRepository locations;
    private readonly IReservationService reservations;
    private readonly IStockLedgerPoster poster;
    private readonly ICompanyContext company;
    private readonly IClock clock;

    public IssueServicePartCommandHandler(IServiceRepository services, IStockLocationRepository locations,
        IReservationService reservations, IStockLedgerPoster poster, ICompanyContext company, IClock clock)
    {
        this.services = services; this.locations = locations; this.reservations = reservations;
        this.poster = poster; this.company = company; this.clock = clock;
    }

    public async Task<Guid> HandleAsync(IssueServicePartCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ServicePartUsage? existing = await services.FindPartUsageByOperationIdAsync(command.OperationId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.CompanyId != command.CompanyId || existing.RepairJobId != command.RepairJobId ||
                existing.ItemId != command.ItemId || existing.ItemVariantId != command.ItemVariantId ||
                existing.Quantity != command.Quantity)
                throw new InvalidOperationException("The service-part operation was replayed with different content.");
            return existing.Id;
        }

        if (company.CompanyId is not { } active || active != command.CompanyId)
            throw new InvalidOperationException("The service company is not the active company.");
        if ((command.ItemId is null) == (command.ItemVariantId is null))
            throw new ArgumentException("A service part must identify exactly one item or variant.");
        Quantity quantity = new(command.Quantity, command.UnitOfMeasure);
        StockLocation location = await locations.FindAsync(command.LocationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Service part stock location not found.");
        ReserveOutcome hold = await reservations.ReserveAsync(location.Id, command.ItemId, command.ItemVariantId, quantity,
            ReservationSource.ServicePart, command.RepairJobId, intentId: command.OperationId, legId: command.OperationId,
            reason: "Service repair part", cancellationToken: cancellationToken).ConfigureAwait(false);
        if (hold.ReservationId is null || hold.Shortfall.Value > 0m)
        {
            if (hold.ReservationId is Guid partial) await reservations.ReleaseAsync(partial, "Service part shortfall", cancellationToken).ConfigureAwait(false);
            throw InventoryRuleException.InsufficientAvailable(hold.Held, quantity);
        }
        try
        {
            StockLedgerEntry entry = await poster.IssueForServicePartAsync(location, command.ItemId, command.ItemVariantId,
                quantity, command.OperationId, cancellationToken).ConfigureAwait(false);
            await reservations.ConsumeAsync(hold.ReservationId.Value, command.OperationId, cancellationToken).ConfigureAwait(false);
            ServicePartUsage usage = ServicePartUsage.Issue(location.TenantId, location.StoreId, command.CompanyId,
                command.RepairJobId, command.OperationId, command.ItemId, command.ItemVariantId, quantity.Value,
                entry.UnitCost.Amount, entry.UnitCost.Currency, clock.UtcNow);
            services.Add(usage);
            return usage.Id;
        }
        catch
        {
            await reservations.ReleaseAsync(hold.ReservationId.Value, "Service part issue failed", cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
