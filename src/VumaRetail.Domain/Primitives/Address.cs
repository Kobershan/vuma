namespace VumaRetail.Domain.Primitives;

/// <summary>
/// A structured postal address. Introduced in Stage 06 (ADR-037) so a store's trading address and a
/// business partner's address are the same shape rather than two free-text fields that drift apart.
/// </summary>
/// <remarks>
/// A value object with no identity of its own — two addresses with the same fields are the same
/// address. <see cref="CountryCode"/> is ISO 3166-1 alpha-2 rather than a free-text country name, so
/// it sorts, filters and validates the same way everywhere it appears.
/// </remarks>
/// <param name="Line1">Street number and name, or the first line of the address.</param>
/// <param name="City">The city or town.</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2, upper case, for example <c>ZA</c>.</param>
/// <param name="Line2">A second address line — unit, floor, complex — or <c>null</c>.</param>
/// <param name="Region">Province, state or region, or <c>null</c> where the country has none.</param>
/// <param name="PostalCode">The postal or ZIP code, or <c>null</c> where the address has none.</param>
public sealed record Address(
    string Line1,
    string City,
    string CountryCode,
    string? Line2 = null,
    string? Region = null,
    string? PostalCode = null)
{
    /// <summary>
    /// Builds an address, upper-casing the country code and trimming every field.
    /// </summary>
    /// <param name="line1">Street number and name, or the first line of the address.</param>
    /// <param name="city">The city or town.</param>
    /// <param name="countryCode">ISO 3166-1 alpha-2, for example <c>za</c> or <c>ZA</c>.</param>
    /// <param name="line2">A second address line, or <c>null</c>.</param>
    /// <param name="region">Province, state or region, or <c>null</c>.</param>
    /// <param name="postalCode">The postal or ZIP code, or <c>null</c>.</param>
    /// <exception cref="ArgumentException">A required field is blank, or the country code is not two letters.</exception>
    public static Address Create(
        string line1,
        string city,
        string countryCode,
        string? line2 = null,
        string? region = null,
        string? postalCode = null)
    {
        string normalizedCountry = Require(countryCode, nameof(countryCode)).ToUpperInvariant();

        if (normalizedCountry.Length != 2 || !normalizedCountry.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException(
                "A country code is two letters, ISO 3166-1 alpha-2 (for example ZA).",
                nameof(countryCode));
        }

        return new Address(
            Require(line1, nameof(line1)),
            Require(city, nameof(city)),
            normalizedCountry,
            Trim(line2),
            Trim(region),
            Trim(postalCode));
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
