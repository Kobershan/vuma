#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Logistics;

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class Shipment : Entity
{
    private Shipment(Guid tenantId, Guid? storeId, Guid? orderId, Guid? shipmentConfirmationId, Guid? carrierId, string number, string? trackingNumber, string addressLine1, string? addressLine2, string city, string? postalCode, string country) : base(tenantId, storeId)
    { OrderId = orderId; ShipmentConfirmationId = shipmentConfirmationId; CarrierId = carrierId; Number = number; TrackingNumber = trackingNumber; AddressLine1 = addressLine1; AddressLine2 = addressLine2; City = city; PostalCode = postalCode; Country = country; Status = LogisticsShipmentStatus.Planned; }
    private Shipment() { }
    public Guid? OrderId { get; private set; }
    public Guid? ShipmentConfirmationId { get; private set; }
    public Guid? CarrierId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public string? TrackingNumber { get; private set; }
    public string AddressLine1 { get; private set; } = string.Empty;
    public string? AddressLine2 { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string? PostalCode { get; private set; }
    public string Country { get; private set; } = string.Empty;
    public LogisticsShipmentStatus Status { get; private set; }
    public DateTimeOffset? DispatchedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public static Shipment Create(Guid tenantId, Guid? storeId, string number, Guid? orderId, Guid? confirmationId, Guid? carrierId, string? trackingNumber, string addressLine1, string? addressLine2, string city, string? postalCode, string country)
    {
        if (tenantId == Guid.Empty) { throw new ArgumentException("Tenant is required.", nameof(tenantId)); }
        if (string.IsNullOrWhiteSpace(number) || number.Trim().Length > 64) { throw new ArgumentException("Shipment number is required and must be 64 characters or fewer.", nameof(number)); }
        if (string.IsNullOrWhiteSpace(addressLine1) || string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(country)) { throw new ArgumentException("A delivery address is required."); }
        return new Shipment(tenantId, storeId, orderId, confirmationId, carrierId, number.Trim(), Clean(trackingNumber, 128), addressLine1.Trim(), Clean(addressLine2, 256), city.Trim(), Clean(postalCode, 32), country.Trim().ToUpperInvariant());
    }
    public void Dispatch(DateTimeOffset at) { if (Status is LogisticsShipmentStatus.Delivered or LogisticsShipmentStatus.Cancelled) { throw new InvalidOperationException("Shipment cannot be dispatched in its current state."); } Status = LogisticsShipmentStatus.InTransit; DispatchedAt = at; }
    public void MarkException() { if (Status == LogisticsShipmentStatus.Cancelled) { throw new InvalidOperationException("Cancelled shipment cannot be changed."); } Status = LogisticsShipmentStatus.Exception; }
    public void MarkDelivered(DateTimeOffset at) { if (Status == LogisticsShipmentStatus.Cancelled) { throw new InvalidOperationException("Cancelled shipment cannot be delivered."); } Status = LogisticsShipmentStatus.Delivered; DeliveredAt = at; }
    public void Cancel() { if (Status == LogisticsShipmentStatus.Delivered) { throw new InvalidOperationException("Delivered shipment cannot be cancelled."); } Status = LogisticsShipmentStatus.Cancelled; }
    private static string? Clean(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
