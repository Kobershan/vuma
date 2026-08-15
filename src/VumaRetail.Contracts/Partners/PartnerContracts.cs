using VumaRetail.Contracts.Platform;

namespace VumaRetail.Contracts.Partners;

/// <summary>Creates a new partner — a supplier, a customer, or both.</summary>
/// <param name="Code">The partner's unique code within the tenant, upper-cased at creation.</param>
/// <param name="Name">The partner's trading name.</param>
/// <param name="Type">
/// <c>Customer</c>, <c>Supplier</c>, or <c>Customer, Supplier</c> for a flags combination. Never empty.
/// </param>
/// <param name="Address">The partner's address, or <c>null</c>.</param>
/// <param name="Email">An email address, or <c>null</c>.</param>
/// <param name="Phone">A phone number, or <c>null</c>.</param>
/// <param name="TaxNumber">A tax registration number, or <c>null</c>.</param>
public sealed record CreatePartnerRequest(
    string Code,
    string Name,
    string Type,
    AddressDto? Address = null,
    string? Email = null,
    string? Phone = null,
    string? TaxNumber = null);

/// <summary>Updates a partner's descriptive fields.</summary>
/// <param name="Name">The new trading name.</param>
/// <param name="Address">The new address, or <c>null</c> to clear it.</param>
/// <param name="Email">The new email, or <c>null</c> to clear it.</param>
/// <param name="Phone">The new phone number, or <c>null</c> to clear it.</param>
/// <param name="TaxNumber">The new tax number, or <c>null</c> to clear it.</param>
public sealed record UpdatePartnerDetailsRequest(
    string Name,
    AddressDto? Address = null,
    string? Email = null,
    string? Phone = null,
    string? TaxNumber = null);

/// <summary>A partner, as returned by the API.</summary>
/// <param name="Id">The partner's id.</param>
/// <param name="Code">The partner's code.</param>
/// <param name="Name">The partner's trading name.</param>
/// <param name="Type">A comma-separated list of <c>Customer</c> and/or <c>Supplier</c>.</param>
/// <param name="Address">The partner's address, or <c>null</c>.</param>
/// <param name="Email">The partner's email, or <c>null</c>.</param>
/// <param name="Phone">The partner's phone number, or <c>null</c>.</param>
/// <param name="TaxNumber">The partner's tax number, or <c>null</c>.</param>
/// <param name="IsActive">Whether the partner may still be traded with.</param>
public sealed record PartnerResponse(
    Guid Id,
    string Code,
    string Name,
    string Type,
    AddressDto? Address,
    string? Email,
    string? Phone,
    string? TaxNumber,
    bool IsActive);

/// <summary>A newly created partner's id.</summary>
/// <param name="Id">The partner.</param>
public sealed record PartnerIdResponse(Guid Id);
