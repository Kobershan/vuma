namespace VumaRetail.Domain.Orders;

/// <summary>
/// A delivery's geography as captured on the order, for Stage 13b's consolidated pick waves
/// (ADR-113). A snapshot, not a reference: normalising rules live here, and a later address edit
/// never rewrites it, so a built wave stays reproducible from its own snapshot.
/// </summary>
/// <param name="Province">Province, state or region. Empty when the address carried none.</param>
/// <param name="City">The city or town.</param>
/// <param name="Suburb">The suburb, when captured. Empty when the address carried none.</param>
/// <param name="PostalCode">The postal or ZIP code. Empty when the address carried none.</param>
public sealed record DeliveryGeography(
    string Province,
    string City,
    string Suburb,
    string PostalCode)
{
    /// <summary>
    /// Snapshots an address into wave-grouping geography. Trims every part; missing parts become
    /// empty strings so grouping never sees a null. Case is preserved as captured — waves group
    /// case-insensitively.
    /// </summary>
    /// <param name="address">The delivery address.</param>
    /// <param name="suburb">The suburb, when captured separately from the address lines.</param>
    public static DeliveryGeography Snapshot(Primitives.Address address, string? suburb = null)
    {
        ArgumentNullException.ThrowIfNull(address);

        return new DeliveryGeography(
            (address.Region ?? string.Empty).Trim(),
            address.City.Trim(),
            (suburb ?? string.Empty).Trim(),
            (address.PostalCode ?? string.Empty).Trim());
    }
}
