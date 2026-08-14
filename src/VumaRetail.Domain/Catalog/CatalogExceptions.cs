using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Catalog;

/// <summary>Something catalog-owned was asked for that does not exist, or is not this tenant's.</summary>
/// <param name="what">What was being looked for, for example <c>item</c>.</param>
/// <param name="id">The identifier that found nothing.</param>
public sealed class CatalogNotFoundException(string what, Guid id)
    : DomainException("CATALOG_NOT_FOUND", $"No {what} with id {id}.", DomainProblemKind.NotFound);

/// <summary>Something catalog-owned must be unique and is not.</summary>
/// <param name="code">The stable machine-readable code for the specific collision.</param>
/// <param name="message">What collided, in words.</param>
public sealed class CatalogConflictException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Conflict)
{
    /// <summary>An item code already used in this tenant.</summary>
    /// <param name="code">The code.</param>
    public static CatalogConflictException ItemCode(string code)
        => new("CATALOG_ITEM_CODE_TAKEN", $"Item code '{code}' is already used in this tenant.");

    /// <summary>A variant SKU already used in this tenant.</summary>
    /// <param name="sku">The SKU.</param>
    public static CatalogConflictException VariantSku(string sku)
        => new("CATALOG_VARIANT_SKU_TAKEN", $"SKU '{sku}' is already used in this tenant.");

    /// <summary>A unit-of-measure code already used in this tenant.</summary>
    /// <param name="code">The code.</param>
    public static CatalogConflictException UnitOfMeasureCode(string code)
        => new("CATALOG_UOM_CODE_TAKEN", $"Unit of measure code '{code}' is already used in this tenant.");

    /// <summary>
    /// A barcode value already used somewhere in this tenant's catalog, regardless of symbology — a
    /// scanner reads a code, not a type.
    /// </summary>
    /// <param name="code">The barcode value.</param>
    public static CatalogConflictException BarcodeTaken(string code)
        => new("CATALOG_BARCODE_TAKEN", $"Barcode '{code}' is already assigned in this tenant's catalog.");
}

/// <summary>A catalog business rule was broken.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What the rule says.</param>
public sealed class CatalogRuleException(string code, string message) : DomainException(code, message)
{
    /// <summary>
    /// A derived unit of measure was asked to convert to another derived unit rather than to a base
    /// unit. Conversion is one level, never a graph (<see cref="UnitOfMeasure"/>'s own remarks).
    /// </summary>
    /// <param name="code">The unit that is not itself a base unit.</param>
    public static CatalogRuleException UnitOfMeasureMustConvertToABaseUnit(string code)
        => new(
            "CATALOG_UOM_NOT_A_BASE_UNIT",
            $"'{code}' is not a base unit of measure. A derived unit converts to a base unit only — "
            + "one level, never a chain.");

    /// <summary>A conversion factor was zero or negative.</summary>
    public static CatalogRuleException ConversionFactorMustBePositive()
        => new("CATALOG_UOM_FACTOR_NOT_POSITIVE", "A conversion factor must be greater than zero.");

    /// <summary>
    /// A barcode was asked to attach directly to an item that already has at least one variant. An
    /// item sells either "as itself" or "as a variant", never both at once — once it has a variant,
    /// every barcode belongs on a variant.
    /// </summary>
    /// <param name="itemCode">The item.</param>
    public static CatalogRuleException BarcodeCannotAttachToItemWithVariants(string itemCode)
        => new(
            "CATALOG_BARCODE_ITEM_HAS_VARIANTS",
            $"Item '{itemCode}' has one or more variants; attach the barcode to a variant instead.");

    /// <summary>
    /// A variant was asked to be added to an item that already carries a barcode of its own. The item
    /// would then sell both "as itself" and "as a variant", which the domain refuses to allow.
    /// </summary>
    /// <param name="itemCode">The item.</param>
    public static CatalogRuleException ItemHasDirectBarcodesCannotAddVariant(string itemCode)
        => new(
            "CATALOG_ITEM_HAS_DIRECT_BARCODES",
            $"Item '{itemCode}' has a barcode attached directly to it; remove it before adding a variant.");

    /// <summary>An item's base unit of measure must belong to the same tenant and be active.</summary>
    /// <param name="unitOfMeasureId">The unit of measure that could not be used.</param>
    public static CatalogRuleException UnitOfMeasureNotAvailable(Guid unitOfMeasureId)
        => new(
            "CATALOG_UOM_NOT_AVAILABLE",
            $"Unit of measure {unitOfMeasureId} does not exist in this tenant, or is not active.");
}
