namespace VumaRetail.Contracts.Catalog;

/// <summary>Creates a unit of measure — a base unit, or one that converts to an existing base unit.</summary>
/// <param name="Code">The short code, upper-cased, for example <c>EA</c> or <c>BOX12</c>.</param>
/// <param name="Name">The unit's name.</param>
/// <param name="Type">
/// What kind of measure this is. Ignored when <paramref name="BaseUnitOfMeasureId"/> is set — a
/// derived unit takes its kind from the base unit it converts to.
/// </param>
/// <param name="BaseUnitOfMeasureId">The base unit this converts to, or <c>null</c> to define a base unit itself.</param>
/// <param name="ConversionFactorToBase">How many base units one of this is worth. Required when converting.</param>
public sealed record CreateUnitOfMeasureRequest(
    string Code,
    string Name,
    string Type,
    Guid? BaseUnitOfMeasureId = null,
    decimal? ConversionFactorToBase = null);

/// <summary>A unit of measure, as returned by the API.</summary>
/// <param name="Id">The unit's id.</param>
/// <param name="Code">The short code.</param>
/// <param name="Name">The unit's name.</param>
/// <param name="Type">What kind of measure this is.</param>
/// <param name="BaseUnitOfMeasureId">The base unit this converts to, or <c>null</c> if this is itself a base unit.</param>
/// <param name="ConversionFactorToBase">How many base units one of this is worth.</param>
/// <param name="IsActive">Whether the unit may still be used on new items.</param>
public sealed record UnitOfMeasureResponse(
    Guid Id,
    string Code,
    string Name,
    string Type,
    Guid? BaseUnitOfMeasureId,
    decimal ConversionFactorToBase,
    bool IsActive);

/// <summary>Creates a new item.</summary>
/// <param name="Code">The item's unique code within the tenant, upper-cased at creation.</param>
/// <param name="Name">The item's name.</param>
/// <param name="ItemType">Stocked, service, or non-stock.</param>
/// <param name="UnitOfMeasureId">The base unit of measure this item is counted in.</param>
/// <param name="Description">An optional longer description.</param>
/// <param name="TaxClassCode">An optional tax class code, resolved by Stage 07's posting rules engine.</param>
public sealed record CreateItemRequest(
    string Code,
    string Name,
    string ItemType,
    Guid UnitOfMeasureId,
    string? Description = null,
    string? TaxClassCode = null);

/// <summary>Updates an item's descriptive fields.</summary>
/// <param name="Name">The new name.</param>
/// <param name="Description">The new description, or <c>null</c> to clear it.</param>
/// <param name="TaxClassCode">The new tax class code, or <c>null</c> to clear it.</param>
public sealed record UpdateItemDetailsRequest(string Name, string? Description = null, string? TaxClassCode = null);

/// <summary>Adds a variant to an existing item.</summary>
/// <param name="Sku">The variant's unique SKU within the tenant, upper-cased at creation.</param>
/// <param name="Attributes">The variant's attribute pairs, for example <c>Size: M</c>.</param>
public sealed record CreateItemVariantRequest(string Sku, IReadOnlyList<VariantAttributeDto> Attributes);

/// <summary>One named attribute of a variant, for example <c>Size: M</c>.</summary>
/// <param name="Name">The attribute's name.</param>
/// <param name="Value">The attribute's value.</param>
public sealed record VariantAttributeDto(string Name, string Value);

/// <summary>An item, as returned by the API.</summary>
/// <param name="Id">The item's id.</param>
/// <param name="Code">The item's code.</param>
/// <param name="Name">The item's name.</param>
/// <param name="Description">The item's description, or <c>null</c>.</param>
/// <param name="ItemType">Stocked, service, or non-stock.</param>
/// <param name="UnitOfMeasureId">The base unit of measure.</param>
/// <param name="TaxClassCode">The tax class code, or <c>null</c>.</param>
/// <param name="IsActive">Whether the item may still be sold or received.</param>
/// <param name="HasVariants">Whether the item sells only through its variants.</param>
public sealed record ItemResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string ItemType,
    Guid UnitOfMeasureId,
    string? TaxClassCode,
    bool IsActive,
    bool HasVariants);

/// <summary>A variant, as returned by the API.</summary>
/// <param name="Id">The variant's id.</param>
/// <param name="ItemId">The parent item.</param>
/// <param name="Sku">The variant's SKU.</param>
/// <param name="Attributes">The variant's attribute pairs, in order.</param>
/// <param name="IsActive">Whether the variant may still be sold or received.</param>
public sealed record ItemVariantResponse(
    Guid Id,
    Guid ItemId,
    string Sku,
    IReadOnlyList<VariantAttributeDto> Attributes,
    bool IsActive);

/// <summary>Attaches a new barcode to an item or a variant. Exactly one of the two owners must be set.</summary>
/// <param name="ItemId">The item, when the item has no variants. Mutually exclusive with <paramref name="ItemVariantId"/>.</param>
/// <param name="ItemVariantId">The variant. Mutually exclusive with <paramref name="ItemId"/>.</param>
/// <param name="Code">The scanned value.</param>
/// <param name="Symbology">What kind of barcode this is.</param>
/// <param name="IsPrimary">
/// Whether this should become the owner's primary barcode. Always true for an owner's first barcode,
/// regardless of what is sent here.
/// </param>
public sealed record AddBarcodeRequest(
    Guid? ItemId,
    Guid? ItemVariantId,
    string Code,
    string Symbology,
    bool IsPrimary = false);

/// <summary>A barcode, as returned by the API.</summary>
/// <param name="Id">The barcode's id.</param>
/// <param name="Code">The scanned value.</param>
/// <param name="Symbology">What kind of barcode this is.</param>
/// <param name="ItemId">The item this identifies directly, or <c>null</c>.</param>
/// <param name="ItemVariantId">The variant this identifies, or <c>null</c>.</param>
/// <param name="IsPrimary">Whether this is the owner's primary barcode.</param>
public sealed record BarcodeResponse(
    Guid Id,
    string Code,
    string Symbology,
    Guid? ItemId,
    Guid? ItemVariantId,
    bool IsPrimary);

/// <summary>A newly created unit of measure's id.</summary>
/// <param name="Id">The unit of measure.</param>
public sealed record UnitOfMeasureIdResponse(Guid Id);

/// <summary>A newly created item's id.</summary>
/// <param name="Id">The item.</param>
public sealed record ItemIdResponse(Guid Id);

/// <summary>A newly created variant's id.</summary>
/// <param name="Id">The variant.</param>
public sealed record ItemVariantIdResponse(Guid Id);

/// <summary>A newly created barcode's id.</summary>
/// <param name="Id">The barcode.</param>
public sealed record BarcodeIdResponse(Guid Id);
