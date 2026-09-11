namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>Stable saga type for reserving a transfer's source-company stock.</summary>
public static class TransferReservationSaga
{
    /// <summary>The durable registry intent type.</summary>
    public const string IntentType = "stage22-transfer-reservation";
}

/// <summary>Stable saga type for shipping a transfer's source-company stock.</summary>
public static class TransferShipmentSaga
{
    /// <summary>The durable registry intent type.</summary>
    public const string IntentType = "stage22-transfer-shipment";
}

/// <summary>Immutable source-company data for one transfer shipment leg.</summary>
public sealed record TransferShipmentPayload(
    Guid TenantId,
    Guid TransferId,
    Guid SenderCompanyId,
    IReadOnlyList<TransferReservationLinePayload> Lines);

/// <summary>Immutable company-local reservation data carried by a transfer saga.</summary>
public sealed record TransferReservationLinePayload(
    Guid LineId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure,
    Guid? ReceiverLocationId = null,
    string? BatchReference = null,
    DateOnly? ExpiryDate = null,
    string? SerialNumber = null);

/// <summary>Sanitised payload for one transfer's source reservation leg.</summary>
public sealed record TransferReservationPayload(
    Guid TenantId,
    Guid TransferId,
    Guid SenderCompanyId,
    IReadOnlyList<TransferReservationLinePayload> Lines);

/// <summary>Stable saga type for receiving a transfer into the receiver's company database.</summary>
public static class TransferReceiptSaga
{
    /// <summary>The durable registry intent type.</summary>
    public const string IntentType = "stage22-transfer-receipt";
}

/// <summary>One delta receipt, with the sender cost fixed at shipment.</summary>
public sealed record TransferReceiptLinePayload(
    Guid LineId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitCost,
    string Currency,
    string? BatchReference = null,
    DateOnly? ExpiryDate = null,
    string? SerialNumber = null);

/// <summary>Receiver-company payload for one cumulative transfer receipt.</summary>
public sealed record TransferReceiptPayload(
    Guid TenantId,
    Guid TransferId,
    Guid ReceiverCompanyId,
    IReadOnlyList<TransferReceiptLinePayload> Lines);
