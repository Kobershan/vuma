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
public sealed record RecordPodCommand(Guid ShipmentId, Guid? StopId, ProofOfDeliveryOutcome Outcome, string RecipientName, string? SignatureHash, string? PhotoBlobKey, double? Latitude, double? Longitude, DateTimeOffset DeliveredAt, string? Notes) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateDeliveryRunCommand(Guid? StoreId, Guid? CarrierId, string RunNumber, DateOnly PlannedDate, string? DriverName, string? VehicleRegistration) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record AddDeliveryStopCommand(Guid RunId, Guid ShipmentId, int Sequence) : ICommand<Guid>;

public sealed class LogisticsCommandHandler(ILogisticsRepository repo, ITenantContext tenant, IClock clock) :
    ICommandHandler<CreateCarrierCommand, Guid>, ICommandHandler<CreateShipmentCommand, Guid>, ICommandHandler<DispatchShipmentCommand, Unit>, ICommandHandler<RecordPodCommand, Guid>, ICommandHandler<CreateDeliveryRunCommand, Guid>, ICommandHandler<AddDeliveryStopCommand, Guid>
{
    public Task<Guid> HandleAsync(CreateCarrierCommand c, CancellationToken ct = default) { var x = Carrier.Create(tenant.TenantId, c.Code, c.Name, c.Phone); repo.Add(x); return Task.FromResult(x.Id); }
    public Task<Guid> HandleAsync(CreateShipmentCommand c, CancellationToken ct = default) { var x = Shipment.Create(tenant.TenantId, c.StoreId, c.Number, c.OrderId, c.ShipmentConfirmationId, c.CarrierId, c.TrackingNumber, c.AddressLine1, c.AddressLine2, c.City, c.PostalCode, c.Country); repo.Add(x); return Task.FromResult(x.Id); }
    public async Task<Unit> HandleAsync(DispatchShipmentCommand c, CancellationToken ct = default) { var x = await repo.FindShipmentAsync(c.ShipmentId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Shipment not found."); x.Dispatch(clock.UtcNow); return Unit.Value; }
    public async Task<Guid> HandleAsync(RecordPodCommand c, CancellationToken ct = default) { var x = await repo.FindShipmentAsync(c.ShipmentId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Shipment not found."); if (await repo.FindPodAsync(c.ShipmentId, ct).ConfigureAwait(false) is not null) throw new InvalidOperationException("Proof of delivery already exists."); var pod = ProofOfDelivery.Record(tenant.TenantId, x.StoreId, c.ShipmentId, c.StopId, c.Outcome, c.RecipientName, c.SignatureHash, c.PhotoBlobKey, c.Latitude, c.Longitude, c.DeliveredAt, c.Notes); repo.Add(pod); x.MarkDelivered(c.DeliveredAt); return pod.Id; }
    public Task<Guid> HandleAsync(CreateDeliveryRunCommand c, CancellationToken ct = default) { var x = DeliveryRun.Create(tenant.TenantId, c.StoreId, c.CarrierId, c.RunNumber, c.PlannedDate, c.DriverName, c.VehicleRegistration); repo.Add(x); return Task.FromResult(x.Id); }
    public async Task<Guid> HandleAsync(AddDeliveryStopCommand c, CancellationToken ct = default) { var run = await repo.FindRunAsync(c.RunId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Delivery run not found."); var shipment = await repo.FindShipmentAsync(c.ShipmentId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Shipment not found."); var x = DeliveryStop.Create(tenant.TenantId, run.StoreId, run.Id, shipment.Id, c.Sequence, shipment.AddressLine1, shipment.City); repo.Add(x); return x.Id; }
}
