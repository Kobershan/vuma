#pragma warning disable CS1591
#pragma warning disable CA1062
#pragma warning disable IDE0011
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Logistics;

namespace VumaRetail.Application.Logistics;

[CommandSideEffect(SideEffect.Write)]
public sealed record CreateCarrierCommand(string Code, string Name, string? Phone) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateShipmentCommand(Guid? StoreId, Guid? OrderId, Guid? ShipmentConfirmationId, Guid? CarrierId, string Number, string? TrackingNumber, string AddressLine1, string? AddressLine2, string City, string? PostalCode, string Country) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record DispatchShipmentCommand(Guid ShipmentId) : ICommand;
 [CommandSideEffect(SideEffect.Write)]
public sealed record MarkShipmentExceptionCommand(Guid ShipmentId) : ICommand;
 [CommandSideEffect(SideEffect.Write)]
public sealed record CancelShipmentCommand(Guid ShipmentId) : ICommand;
[CommandSideEffect(SideEffect.Write)]
public sealed record RecordPodCommand(Guid ShipmentId, Guid? StopId, ProofOfDeliveryOutcome Outcome, string RecipientName, string? SignatureHash, string? PhotoBlobKey, double? Latitude, double? Longitude, DateTimeOffset DeliveredAt, string? Notes) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateDeliveryRunCommand(Guid? StoreId, Guid? CarrierId, string RunNumber, DateOnly PlannedDate, string? DriverName, string? VehicleRegistration) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record AddDeliveryStopCommand(Guid RunId, Guid ShipmentId, int Sequence) : ICommand<Guid>;
 [CommandSideEffect(SideEffect.Write)]
public sealed record DispatchDeliveryRunCommand(Guid RunId) : ICommand;
 [CommandSideEffect(SideEffect.Write)]
public sealed record CompleteDeliveryRunCommand(Guid RunId) : ICommand;
 [CommandSideEffect(SideEffect.Write)]
public sealed record CancelDeliveryRunCommand(Guid RunId) : ICommand;
 [CommandSideEffect(SideEffect.Write)]
public sealed record StartDeliveryStopCommand(Guid StopId) : ICommand;
 [CommandSideEffect(SideEffect.Write)]
public sealed record CompleteDeliveryStopCommand(Guid StopId, bool Successful) : ICommand;

public sealed class LogisticsCommandHandler(ILogisticsRepository repo, ITenantContext tenant, IClock clock) :
    ICommandHandler<CreateCarrierCommand, Guid>, ICommandHandler<CreateShipmentCommand, Guid>, ICommandHandler<DispatchShipmentCommand, Unit>, ICommandHandler<MarkShipmentExceptionCommand, Unit>, ICommandHandler<CancelShipmentCommand, Unit>, ICommandHandler<RecordPodCommand, Guid>, ICommandHandler<CreateDeliveryRunCommand, Guid>, ICommandHandler<AddDeliveryStopCommand, Guid>, ICommandHandler<DispatchDeliveryRunCommand, Unit>, ICommandHandler<CompleteDeliveryRunCommand, Unit>, ICommandHandler<CancelDeliveryRunCommand, Unit>, ICommandHandler<StartDeliveryStopCommand, Unit>, ICommandHandler<CompleteDeliveryStopCommand, Unit>
{
    public Task<Guid> HandleAsync(CreateCarrierCommand c, CancellationToken ct = default) { var x = Carrier.Create(tenant.TenantId, c.Code, c.Name, c.Phone); repo.Add(x); return Task.FromResult(x.Id); }
    public async Task<Guid> HandleAsync(CreateShipmentCommand c, CancellationToken ct = default) { ArgumentNullException.ThrowIfNull(c); if (c.CarrierId is Guid carrierId && (await repo.FindCarrierAsync(carrierId, ct).ConfigureAwait(false) is not { IsActive: true })) throw new KeyNotFoundException("Active carrier not found."); var x = Shipment.Create(tenant.TenantId, c.StoreId, c.Number, c.OrderId, c.ShipmentConfirmationId, c.CarrierId, c.TrackingNumber, c.AddressLine1, c.AddressLine2, c.City, c.PostalCode, c.Country); repo.Add(x); return x.Id; }
    public async Task<Unit> HandleAsync(DispatchShipmentCommand c, CancellationToken ct = default) { var x = await repo.FindShipmentAsync(c.ShipmentId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Shipment not found."); x.Dispatch(clock.UtcNow); return Unit.Value; }
    public async Task<Unit> HandleAsync(MarkShipmentExceptionCommand c, CancellationToken ct = default) { var x = await repo.FindShipmentAsync(c.ShipmentId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Shipment not found."); x.MarkException(); return Unit.Value; }
    public async Task<Unit> HandleAsync(CancelShipmentCommand c, CancellationToken ct = default) { var x = await repo.FindShipmentAsync(c.ShipmentId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Shipment not found."); x.Cancel(); return Unit.Value; }
    public async Task<Guid> HandleAsync(RecordPodCommand c, CancellationToken ct = default) { var x = await repo.FindShipmentAsync(c.ShipmentId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Shipment not found."); if (await repo.FindPodAsync(c.ShipmentId, ct).ConfigureAwait(false) is not null) throw new InvalidOperationException("Proof of delivery already exists."); if (c.StopId is Guid stopId) { var stop = await repo.FindStopAsync(stopId, ct).ConfigureAwait(false); if (stop is null || stop.ShipmentId != c.ShipmentId) throw new InvalidOperationException("Stop does not belong to shipment."); } var pod = ProofOfDelivery.Record(tenant.TenantId, x.StoreId, c.ShipmentId, c.StopId, c.Outcome, c.RecipientName, c.SignatureHash, c.PhotoBlobKey, c.Latitude, c.Longitude, c.DeliveredAt, c.Notes); repo.Add(pod); if (c.Outcome == ProofOfDeliveryOutcome.Delivered) x.MarkDelivered(c.DeliveredAt); else x.MarkException(); return pod.Id; }
    public async Task<Guid> HandleAsync(CreateDeliveryRunCommand c, CancellationToken ct = default) { ArgumentNullException.ThrowIfNull(c); if (c.CarrierId is Guid carrierId && (await repo.FindCarrierAsync(carrierId, ct).ConfigureAwait(false) is not { IsActive: true })) throw new KeyNotFoundException("Active carrier not found."); var x = DeliveryRun.Create(tenant.TenantId, c.StoreId, c.CarrierId, c.RunNumber, c.PlannedDate, c.DriverName, c.VehicleRegistration); repo.Add(x); return x.Id; }
    public async Task<Guid> HandleAsync(AddDeliveryStopCommand c, CancellationToken ct = default) { var run = await repo.FindRunAsync(c.RunId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Delivery run not found."); var shipment = await repo.FindShipmentAsync(c.ShipmentId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Shipment not found."); if (run.StoreId != shipment.StoreId) throw new InvalidOperationException("Run and shipment must belong to the same store."); var existing = await repo.ListStopsAsync(run.Id, ct).ConfigureAwait(false); if (existing.Any(x => x.ShipmentId == shipment.Id)) throw new InvalidOperationException("Shipment is already assigned to this run."); var x = DeliveryStop.Create(tenant.TenantId, run.StoreId, run.Id, shipment.Id, c.Sequence, shipment.AddressLine1, shipment.City); repo.Add(x); return x.Id; }
    public async Task<Unit> HandleAsync(DispatchDeliveryRunCommand c, CancellationToken ct = default) { var x = await repo.FindRunAsync(c.RunId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Delivery run not found."); x.Dispatch(clock.UtcNow); return Unit.Value; }
    public async Task<Unit> HandleAsync(CompleteDeliveryRunCommand c, CancellationToken ct = default) { var x = await repo.FindRunAsync(c.RunId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Delivery run not found."); x.Complete(clock.UtcNow); return Unit.Value; }
    public async Task<Unit> HandleAsync(CancelDeliveryRunCommand c, CancellationToken ct = default) { var x = await repo.FindRunAsync(c.RunId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Delivery run not found."); x.Cancel(); return Unit.Value; }
    public async Task<Unit> HandleAsync(StartDeliveryStopCommand c, CancellationToken ct = default) { var x = await repo.FindStopAsync(c.StopId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Delivery stop not found."); x.Start(); return Unit.Value; }
    public async Task<Unit> HandleAsync(CompleteDeliveryStopCommand c, CancellationToken ct = default) { var x = await repo.FindStopAsync(c.StopId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Delivery stop not found."); x.Complete(c.Successful); return Unit.Value; }
}
