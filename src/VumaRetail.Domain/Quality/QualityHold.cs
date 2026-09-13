#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Quality;

/// <summary>Quarantines a stock quantity until an authorised quality disposition is recorded.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class QualityHold : Entity, IImmutableRecord
{
    private QualityHold(
        Guid tenantId,
        Guid? storeId,
        Guid companyId,
        Guid operationId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        string reason,
        DateTimeOffset heldAt,
        Guid reservationId,
        string? batchReference,
        DateOnly? expiryDate,
        string? serialNumber)
        : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        OperationId = operationId;
        LocationId = locationId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Quantity = quantity;
        Reason = reason;
        HeldAt = heldAt;
        ReservationId = reservationId;
        BatchReference = batchReference;
        ExpiryDate = expiryDate;
        SerialNumber = serialNumber;
        Status = QualityHoldStatus.Held;
    }

    private QualityHold() { }

    public Guid OperationId { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid? ItemId { get; private set; }
    public Guid? ItemVariantId { get; private set; }
    public Quantity Quantity { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid ReservationId { get; private set; }
    public string? BatchReference { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public string? SerialNumber { get; private set; }
    public QualityHoldStatus Status { get; private set; }
    public DateTimeOffset HeldAt { get; private set; }
    public DateTimeOffset? DisposedAt { get; private set; }
    public string? DispositionReason { get; private set; }

    public static QualityHold Place(
        Guid tenantId,
        Guid? storeId,
        Guid companyId,
        Guid operationId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        string reason,
        DateTimeOffset heldAt,
        Guid reservationId,
        string? batchReference = null,
        DateOnly? expiryDate = null,
        string? serialNumber = null)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || operationId == Guid.Empty
            || locationId == Guid.Empty || reservationId == Guid.Empty)
        {
            throw new ArgumentException("A quality hold requires tenant, company, operation, location and reservation identities.");
        }
        StockItemReference.Validate(itemId, itemVariantId);
        if (quantity.IsNegative || quantity.IsZero)
        {
            throw new ArgumentException("A quality hold quantity must be positive.", nameof(quantity));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Trim().Length > 256)
        {
            throw new ArgumentException("A quality hold reason must be 256 characters or fewer.", nameof(reason));
        }
        (batchReference, expiryDate, serialNumber) = StockTracking.Validate(quantity.Value, batchReference, expiryDate, serialNumber);
        return new QualityHold(tenantId, storeId, companyId, operationId, locationId, itemId, itemVariantId, quantity, reason.Trim(), heldAt, reservationId,
            batchReference, expiryDate, serialNumber);
    }

    public void Release(DateTimeOffset at, string reason)
    {
        if (Status != QualityHoldStatus.Held)
        {
            throw new InvalidOperationException("Only an active quality hold can be released.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = QualityHoldStatus.Released;
        DisposedAt = at;
        DispositionReason = reason.Trim();
    }

    public void Reject(DateTimeOffset at, string reason)
    {
        if (Status != QualityHoldStatus.Held)
        {
            throw new InvalidOperationException("Only an active quality hold can be rejected.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = QualityHoldStatus.Rejected;
        DisposedAt = at;
        DispositionReason = reason.Trim();
    }
}
