using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Warehouse;

/// <summary>
/// A geographic subdivision used by consolidated pick waves and rep territories (Stage 13b).
/// Province → City → Suburb, matching the hierarchy the waves group by.
/// </summary>
public sealed record GeoLocation(
    string Province,
    string? City,
    string? Suburb);
