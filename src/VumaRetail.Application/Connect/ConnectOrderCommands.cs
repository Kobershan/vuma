#pragma warning disable CS1591, IDE0011, CA1062
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Connect;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Connect;

public sealed record ConnectOrderLineInput(string SupplierSku, string Description, decimal Quantity, string UnitOfMeasure, decimal UnitPrice, string Currency);
[CommandSideEffect(SideEffect.Write)]
public sealed record PlaceConnectOrderCommand(Guid ConnectionId, Guid PurchaseOrderId, string OrderNumber, IReadOnlyList<ConnectOrderLineInput> Lines) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record ConfirmConnectOrderCommand(Guid OrderId, IReadOnlyDictionary<Guid, decimal> Quantities, DateTimeOffset PromisedAt) : ICommand;
[CommandSideEffect(SideEffect.Write)]
public sealed record RejectConnectOrderCommand(Guid OrderId, string Reason) : ICommand;
[CommandSideEffect(SideEffect.Write)]
public sealed record DispatchConnectOrderCommand(Guid OrderId, string DispatchNoteNumber, IReadOnlyDictionary<Guid, decimal> Quantities, DateTimeOffset DispatchedAt) : ICommand;
[CommandSideEffect(SideEffect.Write)]
public sealed record ReceiveConnectOrderCommand(Guid OrderId) : ICommand;

public sealed class PlaceConnectOrderCommandValidator : AbstractValidator<PlaceConnectOrderCommand>
{
    public PlaceConnectOrderCommandValidator()
    {
        RuleFor(x => x.ConnectionId).NotEmpty();
        RuleFor(x => x.PurchaseOrderId).NotEmpty();
        RuleFor(x => x.OrderNumber).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Lines).NotEmpty();
    }
}

public sealed class PlaceConnectOrderCommandHandler(
    IConnectOrderRepository orders, ITradingConnectionRepository connections, ITenantContext tenant, IClock clock)
    : ICommandHandler<PlaceConnectOrderCommand, Guid>
{
    public async Task<Guid> HandleAsync(PlaceConnectOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        TradingConnection connection = await connections.FindForTenantAsync(command.ConnectionId, tenant.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Connection not found.");
        if (connection.RetailerTenantId != tenant.TenantId || connection.Status != TradingConnectionStatus.Active)
            throw new UnauthorizedAccessException("Only the active retailer can place an order.");
        ConnectOrder order = ConnectOrder.Place(tenant.TenantId, connection.SupplierTenantId, connection.Id,
            command.PurchaseOrderId, command.OrderNumber, clock.UtcNow);
        foreach (ConnectOrderLineInput input in command.Lines)
            order.AddLine(input.SupplierSku, input.Description, new Quantity(input.Quantity, input.UnitOfMeasure), new Money(input.UnitPrice, input.Currency));
        orders.Add(order);
        return order.Id;
    }
}

public sealed class ConfirmConnectOrderCommandHandler(IConnectOrderRepository orders, ITenantContext tenant)
    : ICommandHandler<ConfirmConnectOrderCommand, Unit>
{
    public async Task<Unit> HandleAsync(ConfirmConnectOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ConnectOrder order = await orders.FindForTenantAsync(command.OrderId, tenant.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Connect order not found.");
        if (order.SupplierTenantId != tenant.TenantId) throw new UnauthorizedAccessException("Only the supplier can confirm an order.");
        order.Confirm(command.Quantities.ToDictionary(x => x.Key, x => new Quantity(x.Value, order.Lines.First(line => line.Id == x.Key).RequestedQuantity.UnitOfMeasure)), command.PromisedAt);
        return Unit.Value;
    }
}

public sealed class RejectConnectOrderCommandHandler(IConnectOrderRepository orders, ITenantContext tenant)
    : ICommandHandler<RejectConnectOrderCommand, Unit>
{
    public async Task<Unit> HandleAsync(RejectConnectOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ConnectOrder order = await orders.FindForTenantAsync(command.OrderId, tenant.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Connect order not found.");
        if (order.SupplierTenantId != tenant.TenantId) throw new UnauthorizedAccessException("Only the supplier can reject an order.");
        order.Reject(command.Reason);
        return Unit.Value;
    }
}

public sealed class DispatchConnectOrderCommandHandler(IConnectOrderRepository orders, ITenantContext tenant)
    : ICommandHandler<DispatchConnectOrderCommand, Unit>
{
    public async Task<Unit> HandleAsync(DispatchConnectOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ConnectOrder order = await orders.FindForTenantAsync(command.OrderId, tenant.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Connect order not found.");
        if (order.SupplierTenantId != tenant.TenantId) throw new UnauthorizedAccessException("Only the supplier can dispatch an order.");
        order.Dispatch(command.DispatchNoteNumber, command.Quantities.ToDictionary(x => x.Key, x => new Quantity(x.Value, order.Lines.First(line => line.Id == x.Key).RequestedQuantity.UnitOfMeasure)), command.DispatchedAt);
        return Unit.Value;
    }
}

public sealed class ReceiveConnectOrderCommandHandler(IConnectOrderRepository orders, ITenantContext tenant)
    : ICommandHandler<ReceiveConnectOrderCommand, Unit>
{
    public async Task<Unit> HandleAsync(ReceiveConnectOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ConnectOrder order = await orders.FindForTenantAsync(command.OrderId, tenant.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Connect order not found.");
        if (order.RetailerTenantId != tenant.TenantId) throw new UnauthorizedAccessException("Only the retailer can receive an order.");
        order.MarkReceived();
        return Unit.Value;
    }
}
