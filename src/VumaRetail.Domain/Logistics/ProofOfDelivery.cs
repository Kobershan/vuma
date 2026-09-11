#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Logistics;

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ProofOfDelivery : Entity, IImmutableRecord
{
    private ProofOfDelivery(Guid tenantId, Guid? storeId, Guid shipmentId, Guid? stopId, ProofOfDeliveryOutcome outcome, string recipientName, string? signatureHash, string? photoBlobKey, double? latitude, double? longitude, DateTimeOffset deliveredAt, string? notes) : base(tenantId, storeId)
    { ShipmentId = shipmentId; StopId = stopId; Outcome = outcome; RecipientName = recipientName; SignatureHash = signatureHash; PhotoBlobKey = photoBlobKey; Latitude = latitude; Longitude = longitude; DeliveredAt = deliveredAt; Notes = notes; }
    private ProofOfDelivery() { }
    public Guid ShipmentId { get; private set; }
    public Guid? StopId { get; private set; }
    public ProofOfDeliveryOutcome Outcome { get; private set; }
    public string RecipientName { get; private set; } = string.Empty;
    public string? SignatureHash { get; private set; }
    public string? PhotoBlobKey { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public DateTimeOffset DeliveredAt { get; private set; }
    public string? Notes { get; private set; }
    public static ProofOfDelivery Record(Guid tenantId, Guid? storeId, Guid shipmentId, Guid? stopId, ProofOfDeliveryOutcome outcome, string recipientName, string? signatureHash, string? photoBlobKey, double? latitude, double? longitude, DateTimeOffset deliveredAt, string? notes)
    {
        if (shipmentId == Guid.Empty || string.IsNullOrWhiteSpace(recipientName)) { throw new ArgumentException("Shipment and recipient are required."); }
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180) { throw new ArgumentException("GPS coordinates are invalid."); }
        return new ProofOfDelivery(tenantId, storeId, shipmentId, stopId, outcome, recipientName.Trim(), Clean(signatureHash, 128), Clean(photoBlobKey, 512), latitude, longitude, deliveredAt, Clean(notes, 1000));
    }
    private static string? Clean(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
