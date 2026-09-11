#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Logistics;

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class DeliveryStop : Entity
{
    private DeliveryStop(Guid tenantId, Guid? storeId, Guid runId, Guid shipmentId, int sequence, string addressLine1, string city) : base(tenantId, storeId)
    { RunId = runId; ShipmentId = shipmentId; Sequence = sequence; AddressLine1 = addressLine1; City = city; Status = DeliveryStopStatus.Planned; }
    private DeliveryStop() { }
    public Guid RunId { get; private set; }
    public Guid ShipmentId { get; private set; }
    public int Sequence { get; private set; }
    public string AddressLine1 { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public DeliveryStopStatus Status { get; private set; }
    public static DeliveryStop Create(Guid tenantId, Guid? storeId, Guid runId, Guid shipmentId, int sequence, string addressLine1, string city)
    {
        if (runId == Guid.Empty || shipmentId == Guid.Empty || sequence < 1 || string.IsNullOrWhiteSpace(addressLine1) || string.IsNullOrWhiteSpace(city))
        {
            throw new ArgumentException("Run, shipment, sequence and address are required.");
        }
        return new DeliveryStop(tenantId, storeId, runId, shipmentId, sequence, addressLine1.Trim(), city.Trim());
    }
    public void Start() { if (Status == DeliveryStopStatus.Planned) { Status = DeliveryStopStatus.OutForDelivery; } }
    public void Complete(bool successful) => Status = successful ? DeliveryStopStatus.Delivered : DeliveryStopStatus.Failed;
}
