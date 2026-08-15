using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Catalog;

/// <summary>
/// A scannable code that identifies an <see cref="Item"/> or an <see cref="ItemVariant"/> at the till.
/// </summary>
/// <remarks>
/// <para>
/// Attaches to exactly one of an item (only while that item has no variants) or a variant — a scanner
/// reads a code and needs exactly one thing to resolve it to, so the two factory methods are the only
/// way to construct one and each enforces its half of the rule.
/// </para>
/// <para>
/// The one entity in this stage that is genuinely hard-deleted rather than deactivated (see
/// <c>docs/DECISIONS.md</c> for the ADR): a barcode carries no history of its own the way a sale line
/// or a stock movement does, and a shop relabelling a product has no use for a "deactivated" alias
/// sitting in every future lookup.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class Barcode : Entity
{
    private Barcode(Guid tenantId, string code, BarcodeSymbology symbology, Guid? itemId, Guid? itemVariantId)
        : base(tenantId)
    {
        Code = code;
        Symbology = symbology;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Barcode()
    {
    }

    /// <summary>The scanned value.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>What kind of barcode this is. Uniqueness ignores this — a scanner reads a code, not a type.</summary>
    public BarcodeSymbology Symbology { get; private set; }

    /// <summary>The item this barcode identifies directly, or <c>null</c> when it belongs to a variant instead.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant this barcode identifies, or <c>null</c> when it belongs to an item directly.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>
    /// True for the one barcode of its owner that is printed on a label and shown first at POS. The
    /// rest are aliases — a case-pack code, a legacy code from a migrated system.
    /// </summary>
    public bool IsPrimary { get; private set; }

    /// <summary>Attaches a new barcode directly to an item.</summary>
    /// <param name="item">
    /// The item. Must not yet have any variants — once it does, every barcode belongs on a variant.
    /// </param>
    /// <param name="code">The scanned value.</param>
    /// <param name="symbology">What kind of barcode this is.</param>
    /// <exception cref="CatalogRuleException"><paramref name="item"/> already has a variant.</exception>
    public static Barcode CreateForItem(Item item, string code, BarcodeSymbology symbology)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.HasVariants)
        {
            throw CatalogRuleException.BarcodeCannotAttachToItemWithVariants(item.Code);
        }

        return new Barcode(item.TenantId, Require(code, nameof(code)), symbology, item.Id, null);
    }

    /// <summary>Attaches a new barcode to a specific variant.</summary>
    /// <param name="variant">The variant.</param>
    /// <param name="code">The scanned value.</param>
    /// <param name="symbology">What kind of barcode this is.</param>
    public static Barcode CreateForVariant(ItemVariant variant, string code, BarcodeSymbology symbology)
    {
        ArgumentNullException.ThrowIfNull(variant);

        return new Barcode(variant.TenantId, Require(code, nameof(code)), symbology, null, variant.Id);
    }

    /// <summary>Marks this barcode primary. Callers use <see cref="SetPrimary"/> to keep the set consistent.</summary>
    internal void MakePrimary() => IsPrimary = true;

    /// <summary>Demotes this barcode from primary. Callers use <see cref="SetPrimary"/> to keep the set consistent.</summary>
    internal void Demote() => IsPrimary = false;

    /// <summary>
    /// Makes exactly one barcode among a shared owner's siblings primary, atomically demoting whichever
    /// one held that place before.
    /// </summary>
    /// <param name="ownersBarcodes">Every barcode belonging to the same item or variant.</param>
    /// <param name="barcodeId">Which of them becomes primary.</param>
    /// <exception cref="CatalogNotFoundException"><paramref name="barcodeId"/> is not among them.</exception>
    public static void SetPrimary(IReadOnlyCollection<Barcode> ownersBarcodes, Guid barcodeId)
    {
        ArgumentNullException.ThrowIfNull(ownersBarcodes);

        if (!ownersBarcodes.Any(barcode => barcode.Id == barcodeId))
        {
            throw new CatalogNotFoundException("barcode", barcodeId);
        }

        foreach (Barcode barcode in ownersBarcodes)
        {
            if (barcode.Id == barcodeId)
            {
                barcode.MakePrimary();
            }
            else
            {
                barcode.Demote();
            }
        }
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();
}

/// <summary>The kind of a <see cref="Barcode"/>.</summary>
public enum BarcodeSymbology
{
    /// <summary>13-digit European Article Number.</summary>
    Ean13 = 0,

    /// <summary>12-digit Universal Product Code.</summary>
    Upc = 1,

    /// <summary>Variable-length alphanumeric Code 128.</summary>
    Code128 = 2,

    /// <summary>A tenant's own scheme — a case-pack code, a legacy migrated code, a weighed-item prefix.</summary>
    Internal = 3,
}
