using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Logistics;
using VumaRetail.Domain.Logistics;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

public sealed class LogisticsRepository(VumaRetailDbContext context) : ILogisticsRepository
{
    public Task<Carrier?> FindCarrierAsync(Guid id, CancellationToken cancellationToken = default) => context.Carriers.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public async Task<IReadOnlyList<Carrier>> ListCarriersAsync(CancellationToken cancellationToken = default) => await context.Carriers.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
    public Task<Shipment?> FindShipmentAsync(Guid id, CancellationToken cancellationToken = default) => context.Shipments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<Shipment?> FindShipmentByNumberAsync(string number, CancellationToken cancellationToken = default) => context.Shipments.SingleOrDefaultAsync(x => x.Number == number, cancellationToken);
    public async Task<IReadOnlyList<Shipment>> ListShipmentsAsync(LogisticsShipmentStatus? status, CancellationToken cancellationToken = default) => await context.Shipments.AsNoTracking().Where(x => status == null || x.Status == status).OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(cancellationToken);
    public Task<DeliveryRun?> FindRunAsync(Guid id, CancellationToken cancellationToken = default) => context.DeliveryRuns.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public async Task<IReadOnlyList<DeliveryStop>> ListStopsAsync(Guid runId, CancellationToken cancellationToken = default) => await context.DeliveryStops.AsNoTracking().Where(x => x.RunId == runId).OrderBy(x => x.Sequence).ToListAsync(cancellationToken);
    public Task<ProofOfDelivery?> FindPodAsync(Guid shipmentId, CancellationToken cancellationToken = default) => context.ProofsOfDelivery.AsNoTracking().SingleOrDefaultAsync(x => x.ShipmentId == shipmentId, cancellationToken);
    public void Add(Carrier carrier) => context.Carriers.Add(carrier);
    public void Add(Shipment shipment) => context.Shipments.Add(shipment);
    public void Add(DeliveryRun run) => context.DeliveryRuns.Add(run);
    public void Add(DeliveryStop stop) => context.DeliveryStops.Add(stop);
    public void Add(ProofOfDelivery proof) => context.ProofsOfDelivery.Add(proof);
}
