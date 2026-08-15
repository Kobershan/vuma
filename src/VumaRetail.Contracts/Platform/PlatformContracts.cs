namespace VumaRetail.Contracts.Platform;

/// <summary>
/// The wire shape of <c>Domain.Primitives.Address</c> — Stage 06's structured address, shared by
/// <c>Store</c> and <c>Partner</c> (ADR-037).
/// </summary>
/// <param name="Line1">Street number and name, or the first line of the address.</param>
/// <param name="City">The city or town.</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2, for example <c>ZA</c>.</param>
/// <param name="Line2">A second address line, or <c>null</c>.</param>
/// <param name="Region">Province, state or region, or <c>null</c>.</param>
/// <param name="PostalCode">The postal or ZIP code, or <c>null</c>.</param>
public sealed record AddressDto(
    string Line1,
    string City,
    string CountryCode,
    string? Line2 = null,
    string? Region = null,
    string? PostalCode = null);
