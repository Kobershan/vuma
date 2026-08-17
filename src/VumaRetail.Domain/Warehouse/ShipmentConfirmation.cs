using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Warehouse;

/// <summary>
/// The document that records a <see cref="PickWave"/> shipping — the point stock actually leaves the
/// location, per business rule 3.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ShipmentConfirmation : Entity, IImmutableRecord
{
    private ShipmentConfirmation(
        Guid id, Guid tenantId, Guid? storeId, Guid pickWaveId, string? carrier, string? trackingNumber, DateTimeOffset shippedAt)
        : base(id, tenantId, storeId)
    {
        PickWaveId = pickWaveId;
        Carrier = carrier;
        TrackingNumber = trackingNumber;
        ShippedAt = shippedAt;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ShipmentConfirmation()
    {
    }

    /// <summary>The wave that shipped.</summary>
    public Guid PickWaveId { get; private set; }

    /// <summary>The carrier, free text — a real carrier integration is Stage 24's (see the stage document's notes).</summary>
    public string? Carrier { get; private set; }

    /// <summary>The carrier's tracking number, if any.</summary>
    public string? TrackingNumber { get; private set; }

    /// <summary>When the shipment was confirmed, UTC.</summary>
    public DateTimeOffset ShippedAt { get; private set; }

    /// <summary>
    /// Records a shipment. Immutable once created — a correction is a new shipment document, not an
    /// edit.
    /// </summary>
    /// <param name="id">
    /// The document's own id — minted by the caller <em>before</em> this document exists, so the
    /// location-level ledger issue(s) it correlates to can carry it as their reference. The same shape
    /// Stage 08's <c>StockTransfer.Record</c> uses.
    /// </param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store.</param>
    /// <param name="pickWaveId">The wave that shipped.</param>
    /// <param name="carrier">The carrier, free text.</param>
    /// <param name="trackingNumber">The carrier's tracking number, if any.</param>
    /// <param name="shippedAt">When the shipment was confirmed, UTC.</param>
    public static ShipmentConfirmation Ship(
        Guid id, Guid tenantId, Guid? storeId, Guid pickWaveId, string? carrier, string? trackingNumber, DateTimeOffset shippedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A shipment confirmation must be given a pre-minted id.", nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A shipment confirmation must belong to a tenant.", nameof(tenantId));
        }

        if (pickWaveId == Guid.Empty)
        {
            throw new ArgumentException("A shipment confirmation must belong to a wave.", nameof(pickWaveId));
        }

        return new ShipmentConfirmation(
            id, tenantId, storeId, pickWaveId,
            string.IsNullOrWhiteSpace(carrier) ? null : carrier.Trim(),
            string.IsNullOrWhiteSpace(trackingNumber) ? null : trackingNumber.Trim(),
            shippedAt);
    }
}
