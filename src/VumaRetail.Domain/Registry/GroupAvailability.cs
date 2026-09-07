using VumaRetail.Domain.Primitives;

// Registry availability projection for Stage 08c (group availability read model, ADR-119).
#pragma warning disable CS1591
#pragma warning disable IDE0011
namespace VumaRetail.Domain.Registry;

// ========== Stage 08c: Group availability projection ==========

/// <summary>
/// One company's last-published availability for one stock-keeping unit at one location.
/// A projection, never a source: fed by each company database's outbox, carrying AsAt, and
/// never the basis for a commit (ADR-119).
/// </summary>
public sealed class GroupAvailabilityRow
{
    private GroupAvailabilityRow() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string CompanyCode { get; private set; } = string.Empty;
    public Guid LocationId { get; private set; }
    public Guid? ItemId { get; private set; }
    public Guid? ItemVariantId { get; private set; }
    public decimal OnHand { get; private set; }
    public decimal Reserved { get; private set; }
    public decimal InStaging { get; private set; }
    public decimal Available { get; private set; }
    public string UnitOfMeasure { get; private set; } = string.Empty;
    public DateTimeOffset AsAt { get; private set; }

    public static GroupAvailabilityRow Publish(
        Guid tenantId,
        Guid companyId,
        string companyCode,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal onHand,
        decimal reserved,
        decimal inStaging,
        string unitOfMeasure,
        DateTimeOffset asAt)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (companyId == Guid.Empty) throw new ArgumentException("A company is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(companyCode)) throw new ArgumentException("A company code is required.", nameof(companyCode));
        if (locationId == Guid.Empty) throw new ArgumentException("A location is required.", nameof(locationId));
        if (string.IsNullOrWhiteSpace(unitOfMeasure)) throw new ArgumentException("A unit of measure is required.", nameof(unitOfMeasure));
        bool hasItem = itemId is not null && itemId != Guid.Empty;
        bool hasVariant = itemVariantId is not null && itemVariantId != Guid.Empty;
        if (hasItem == hasVariant) throw new ArgumentException("Exactly one of item or variant is required.");
        if (onHand < 0m || reserved < 0m || inStaging < 0m) throw new ArgumentException("Availability figures cannot be negative.");
        if (reserved + inStaging > onHand) throw new ArgumentException("Available cannot be negative.");

        return new GroupAvailabilityRow
        {
            Id = UuidV7.NewGuid(),
            TenantId = tenantId,
            CompanyId = companyId,
            CompanyCode = companyCode.Trim(),
            LocationId = locationId,
            ItemId = hasItem ? itemId : null,
            ItemVariantId = hasVariant ? itemVariantId : null,
            OnHand = onHand,
            Reserved = reserved,
            InStaging = inStaging,
            Available = onHand - reserved - inStaging,
            UnitOfMeasure = unitOfMeasure.Trim(),
            AsAt = asAt,
        };
    }

    public void Refresh(decimal onHand, decimal reserved, decimal inStaging, DateTimeOffset asAt)
    {
        if (onHand < 0m || reserved < 0m || inStaging < 0m) throw new ArgumentException("Availability figures cannot be negative.");
        if (reserved + inStaging > onHand) throw new ArgumentException("Available cannot be negative.");
        OnHand = onHand;
        Reserved = reserved;
        InStaging = inStaging;
        Available = onHand - reserved - inStaging;
        AsAt = asAt;
    }
}

/// <summary>
/// How far the availability relay has read into one company's outbox. The relay is at-least-once
/// and the projection upsert is idempotent, so a crash between applying rows and advancing the
/// cursor replays harmlessly.
/// </summary>
public sealed class GroupAvailabilityCursor
{
    private GroupAvailabilityCursor() { }

    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid LastOutboxRowId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static GroupAvailabilityCursor Start(Guid tenantId, Guid companyId, DateTimeOffset updatedAt)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (companyId == Guid.Empty) throw new ArgumentException("A company is required.", nameof(companyId));
        return new GroupAvailabilityCursor
        {
            TenantId = tenantId,
            CompanyId = companyId,
            LastOutboxRowId = Guid.Empty,
            UpdatedAt = updatedAt,
        };
    }

    public void Advance(Guid outboxRowId, DateTimeOffset updatedAt)
    {
        if (outboxRowId == Guid.Empty) throw new ArgumentException("A cursor needs a real outbox row.", nameof(outboxRowId));
        LastOutboxRowId = outboxRowId;
        UpdatedAt = updatedAt;
    }
}

/// <summary>
/// How long a new reservation hold for one source kind lives before it lapses, per tenant.
/// Absent rows mean the stage defaults (order and pro-forma approval holds 72 hours; transfer
/// and shipment holds never expire). A null <see cref="ExpiryHours"/> is an explicit never.
/// </summary>
public sealed class ReservationExpiryPolicyRow
{
    private ReservationExpiryPolicyRow() { }

    public Guid TenantId { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public int? ExpiryHours { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ReservationExpiryPolicyRow Set(Guid tenantId, string source, int? expiryHours, DateTimeOffset updatedAt)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("A source is required.", nameof(source));
        if (expiryHours is < 0) throw new ArgumentException("An expiry cannot be negative.", nameof(expiryHours));
        return new ReservationExpiryPolicyRow
        {
            TenantId = tenantId,
            Source = source.Trim(),
            ExpiryHours = expiryHours,
            UpdatedAt = updatedAt,
        };
    }

    public void Change(int? expiryHours, DateTimeOffset updatedAt)
    {
        if (expiryHours is < 0) throw new ArgumentException("An expiry cannot be negative.", nameof(expiryHours));
        ExpiryHours = expiryHours;
        UpdatedAt = updatedAt;
    }
}
