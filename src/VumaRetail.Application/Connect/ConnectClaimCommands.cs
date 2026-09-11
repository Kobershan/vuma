#pragma warning disable CS1591, IDE0011, CA1062
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Connect;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Connect;

public sealed record RaiseConnectClaimCommand(Guid OrderId, Guid OrderLineId, string ClaimNumber,
    ConnectClaimReason Reason, decimal Quantity, string UnitOfMeasure, decimal Amount, string Currency, string Description) : ICommand<Guid>;
public sealed record ResolveConnectClaimCommand(Guid ClaimId, bool Credit, string? CreditNoteReference) : ICommand;

public sealed class RaiseConnectClaimCommandValidator : AbstractValidator<RaiseConnectClaimCommand>
{
    public RaiseConnectClaimCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty(); RuleFor(x => x.OrderLineId).NotEmpty();
        RuleFor(x => x.ClaimNumber).NotEmpty().MaximumLength(64); RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0); RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Currency).Length(3); RuleFor(x => x.UnitOfMeasure).NotEmpty().MaximumLength(16);
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed class RaiseConnectClaimCommandHandler(IConnectOrderRepository orders, IConnectClaimRepository claims, ITenantContext tenant, IClock clock)
    : ICommandHandler<RaiseConnectClaimCommand, Guid>
{
    public async Task<Guid> HandleAsync(RaiseConnectClaimCommand command, CancellationToken cancellationToken = default)
    {
        ConnectOrder order = await orders.FindForTenantAsync(command.OrderId, tenant.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Connect order not found.");
        if (order.RetailerTenantId != tenant.TenantId) throw new UnauthorizedAccessException("Only the retailer can raise a claim.");
        ConnectOrderLine line = order.Lines.FirstOrDefault(x => x.Id == command.OrderLineId)
            ?? throw new InvalidOperationException("Connect order line not found.");
        ConnectDeliveryClaim claim = ConnectDeliveryClaim.Raise(tenant.TenantId, order.SupplierTenantId, order.ConnectionId,
            order.Id, line.Id, command.ClaimNumber, command.Reason, new Quantity(command.Quantity, command.UnitOfMeasure),
            new Money(command.Amount, command.Currency), command.Description, clock.UtcNow);
        claims.Add(claim); return claim.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed class ResolveConnectClaimCommandHandler(IConnectClaimRepository claims, ITenantContext tenant, IClock clock)
    : ICommandHandler<ResolveConnectClaimCommand, Unit>
{
    public async Task<Unit> HandleAsync(ResolveConnectClaimCommand command, CancellationToken cancellationToken = default)
    {
        ConnectDeliveryClaim claim = await claims.FindForTenantAsync(command.ClaimId, tenant.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Connect claim not found.");
        if (claim.SupplierTenantId != tenant.TenantId) throw new UnauthorizedAccessException("Only the supplier can resolve a claim.");
        if (command.Credit) claim.IssueCreditNote(command.CreditNoteReference ?? string.Empty, clock.UtcNow);
        else claim.Reject(clock.UtcNow);
        return Unit.Value;
    }
}
