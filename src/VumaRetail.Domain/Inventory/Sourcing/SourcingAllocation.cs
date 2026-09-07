namespace VumaRetail.Domain.Inventory.Sourcing;

/// <summary>
/// A single allocation within a SourcingPlan: per-company, per-location planned quantities.
/// Backorder is explicit, never null — if we couldn't fill the entire demand, the remainder is backordered.
/// </summary>
public sealed class SourcingAllocation
{
    private SourcingAllocation() { }

    public SourcingAllocation(
        Guid companyId,
        Guid locationId,
        decimal plannedQuantity,
        decimal backorderedQuantity)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID is required.", nameof(companyId));
        if (locationId == Guid.Empty) throw new ArgumentException("Location ID is required.", nameof(locationId));
        if (plannedQuantity < 0) throw new ArgumentException("Planned quantity cannot be negative.", nameof(plannedQuantity));
        if (backorderedQuantity < 0) throw new ArgumentException("Backordered quantity cannot be negative.", nameof(backorderedQuantity));
        if (plannedQuantity == 0 && backorderedQuantity == 0)
            throw new ArgumentException("Either planned or backordered quantity must be positive.", nameof(plannedQuantity));

        CompanyId = companyId;
        LocationId = locationId;
        PlannedQuantity = plannedQuantity;
        BackorderedQuantity = backorderedQuantity;
    }

    /// <summary>
    /// Company from which this allocation is sourced.
    /// </summary>
    public Guid CompanyId { get; private set; }

    /// <summary>
    /// Location within the company (warehouse/store).
    /// </summary>
    public Guid LocationId { get; private set; }

    /// <summary>
    /// Quantity planned to be held as a reservation.
    /// </summary>
    public decimal PlannedQuantity { get; private set; }

    /// <summary>
    /// Quantity that could not be sourced and must be backordered.
    /// Explicit data, never null — backorder is a valid outcome, not an error.
    /// </summary>
    public decimal BackorderedQuantity { get; private set; }

    /// <summary>
    /// Total demand this allocation covers: PlannedQuantity + BackorderedQuantity.
    /// </summary>
    public decimal TotalCovered => PlannedQuantity + BackorderedQuantity;
}
