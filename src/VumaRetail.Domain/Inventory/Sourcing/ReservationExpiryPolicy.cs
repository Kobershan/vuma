namespace VumaRetail.Domain.Inventory.Sourcing;

/// <summary>
/// Policy for when reservations expire based on their source document type.
/// Per-tenant overridable; defaults: Order 72h, ProFormaApproval 72h, Transfer none, Shipment none.
/// </summary>
public sealed class ReservationExpiryPolicy
{
    private ReservationExpiryPolicy() { }

    public ReservationExpiryPolicy(
        Guid tenantId,
        ReservationSource sourceDocumentType,
        int? expiryHours)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        if (expiryHours.HasValue && expiryHours.Value < 0)
            throw new ArgumentException("Expiry hours cannot be negative.", nameof(expiryHours));

        TenantId = tenantId;
        SourceDocumentType = sourceDocumentType;
        ExpiryHours = expiryHours; // null = never expires
    }

    public Guid TenantId { get; private set; }
    public ReservationSource SourceDocumentType { get; private set; }
    public int? ExpiryHours { get; private set; }

    /// <summary>
    /// Get the default policies.
    /// </summary>
    public static IReadOnlyList<ReservationExpiryPolicy> GetDefaults(Guid tenantId)
    {
        return new[]
        {
            new ReservationExpiryPolicy(tenantId, ReservationSource.Order, 72),
            new ReservationExpiryPolicy(tenantId, ReservationSource.ProFormaApproval, 72),
            new ReservationExpiryPolicy(tenantId, ReservationSource.Transfer, null),
            new ReservationExpiryPolicy(tenantId, ReservationSource.Shipment, null)
        };
    }
}
