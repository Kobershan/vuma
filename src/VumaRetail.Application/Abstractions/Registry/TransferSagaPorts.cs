namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>Stable saga type for reserving a transfer's source-company stock.</summary>
public static class TransferReservationSaga
{
    /// <summary>The durable registry intent type.</summary>
    public const string IntentType = "stage22-transfer-reservation";
}

/// <summary>Immutable company-local reservation data carried by a transfer saga.</summary>
public sealed record TransferReservationLinePayload(
    Guid LineId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure);

/// <summary>Sanitised payload for one transfer's source reservation leg.</summary>
public sealed record TransferReservationPayload(
    Guid TenantId,
    Guid TransferId,
    Guid SenderCompanyId,
    IReadOnlyList<TransferReservationLinePayload> Lines);
