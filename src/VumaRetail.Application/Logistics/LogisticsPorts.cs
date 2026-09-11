#pragma warning disable CS1591
using VumaRetail.Domain.Logistics;

namespace VumaRetail.Application.Logistics;

public interface ILogisticsRepository
{
    Task<Carrier?> FindCarrierAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Carrier>> ListCarriersAsync(CancellationToken cancellationToken = default);
    Task<Shipment?> FindShipmentAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Shipment?> FindShipmentByNumberAsync(string number, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Shipment>> ListShipmentsAsync(LogisticsShipmentStatus? status, CancellationToken cancellationToken = default);
    Task<DeliveryRun?> FindRunAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryStop>> ListStopsAsync(Guid runId, CancellationToken cancellationToken = default);
    Task<ProofOfDelivery?> FindPodAsync(Guid shipmentId, CancellationToken cancellationToken = default);
    void Add(Carrier carrier);
    void Add(Shipment shipment);
    void Add(DeliveryRun run);
    void Add(DeliveryStop stop);
    void Add(ProofOfDelivery proof);
}
