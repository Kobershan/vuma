namespace VumaRetail.Contracts.Logistics;

public sealed record CreateCarrierRequest(string Code, string Name, string? Phone);
public sealed record CreateShipmentRequest(Guid? StoreId, Guid? OrderId, Guid? ShipmentConfirmationId, Guid? CarrierId, string Number, string? TrackingNumber, string AddressLine1, string? AddressLine2, string City, string? PostalCode, string Country);
public sealed record CreateDeliveryRunRequest(Guid? StoreId, Guid? CarrierId, string RunNumber, DateOnly PlannedDate, string? DriverName, string? VehicleRegistration);
public sealed record AddDeliveryStopRequest(Guid ShipmentId, int Sequence);
public sealed record RecordPodRequest(Guid? StopId, string Outcome, string RecipientName, string? SignatureHash, string? PhotoBlobKey, double? Latitude, double? Longitude, DateTimeOffset DeliveredAt, string? Notes);
public sealed record CarrierResponse(Guid Id, string Code, string Name, string? Phone, bool IsActive);
public sealed record ShipmentResponse(Guid Id, string Number, Guid? OrderId, Guid? CarrierId, string? TrackingNumber, string Status, string AddressLine1, string City, string Country, DateTimeOffset? DispatchedAt, DateTimeOffset? DeliveredAt);
public sealed record DeliveryRunResponse(Guid Id, string RunNumber, DateOnly PlannedDate, Guid? CarrierId, string? DriverName, string? VehicleRegistration, string Status, IReadOnlyList<DeliveryStopResponse> Stops);
public sealed record DeliveryStopResponse(Guid Id, Guid ShipmentId, int Sequence, string AddressLine1, string City, string Status);
public sealed record PodResponse(Guid ShipmentId, Guid? StopId, string Outcome, string RecipientName, string? SignatureHash, string? PhotoBlobKey, double? Latitude, double? Longitude, DateTimeOffset DeliveredAt, string? Notes);
