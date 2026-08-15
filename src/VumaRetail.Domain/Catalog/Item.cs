using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Catalog;

/// <summary>
/// A product or service a tenant sells or stocks. The identity and description every later module
/// reads — no price, no stock, no GL account (Stage 06's own scope note).
/// </summary>
/// <remarks>
/// <para>
/// An item either sells "as itself" or, once it has at least one <see cref="ItemVariant"/>, only
/// through a variant — never both. <see cref="HasVariants"/> is the domain's own record of which
/// state it is in, and <see cref="Barcode"/> reads it before attaching directly to an item.
/// </para>
/// <para>
/// <see cref="UnitOfMeasureId"/> and <see cref="TaxClassCode"/> are both plain values rather than
/// navigation properties: a unit of measure lives in this same schema and is validated by the command
/// handler against the tenant's own units, while a tax class is a string code resolved by Stage 07's
/// posting rules engine (<c>CONVENTIONS.md</c> §2 forbids the cross-schema foreign key that a real
/// reference into finance would need).
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class Item : Entity
{
    private Item(Guid tenantId, string code, string name, ItemType itemType, Guid unitOfMeasureId)
        : base(tenantId)
    {
        Code = code;
        Name = name;
        ItemType = itemType;
        UnitOfMeasureId = unitOfMeasureId;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Item()
    {
    }

    /// <summary>The item's unique code within the tenant, upper-cased, like <c>Store.Code</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The item's name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>A longer free-text description, or <c>null</c>.</summary>
    public string? Description { get; private set; }

    /// <summary>Whether this is a stocked good, a service, or something tracked without stock.</summary>
    public ItemType ItemType { get; private set; }

    /// <summary>The base <see cref="UnitOfMeasure"/> this item is counted in.</summary>
    public Guid UnitOfMeasureId { get; private set; }

    /// <summary>
    /// A tax class code, or <c>null</c>. Stage 07's posting rules engine decides what this code means
    /// in the ledger — this schema names it and nothing more (<c>CLAUDE.md</c> §7 rule 12).
    /// </summary>
    public string? TaxClassCode { get; private set; }

    /// <summary>True while the item may be sold or received. Deactivate, not delete (§7 rule 8).</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// True once the item has at least one <see cref="ItemVariant"/>. Once true, a barcode may no
    /// longer attach directly to the item — see <see cref="Barcode.CreateForItem"/>.
    /// </summary>
    public bool HasVariants { get; private set; }

    /// <summary>Creates a new item.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="code">The item code, upper-cased.</param>
    /// <param name="name">The item's name.</param>
    /// <param name="itemType">Stocked, service, or non-stock.</param>
    /// <param name="unitOfMeasureId">
    /// The base unit of measure. The caller (the command handler) is responsible for having already
    /// confirmed it belongs to this tenant and is active — the same shape as every other cross-aggregate
    /// reference in this codebase (<c>CreateStoreCommandHandler</c>'s code-uniqueness check is the
    /// precedent).
    /// </param>
    /// <param name="description">An optional longer description.</param>
    /// <param name="taxClassCode">An optional tax class code.</param>
    public static Item Create(
        Guid tenantId,
        string code,
        string name,
        ItemType itemType,
        Guid unitOfMeasureId,
        string? description = null,
        string? taxClassCode = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An item must belong to a tenant.", nameof(tenantId));
        }

        if (unitOfMeasureId == Guid.Empty)
        {
            throw new ArgumentException("An item must name a base unit of measure.", nameof(unitOfMeasureId));
        }

        return new Item(
            tenantId,
            Require(code, nameof(code)).ToUpperInvariant(),
            Require(name, nameof(name)),
            itemType,
            unitOfMeasureId)
        {
            Description = Trim(description),
            TaxClassCode = Trim(taxClassCode),
        };
    }

    /// <summary>Updates the item's descriptive fields.</summary>
    /// <param name="name">The new name.</param>
    /// <param name="description">The new description, or <c>null</c> to clear it.</param>
    /// <param name="taxClassCode">The new tax class code, or <c>null</c> to clear it.</param>
    public void SetDetails(string name, string? description, string? taxClassCode)
    {
        Name = Require(name, nameof(name));
        Description = Trim(description);
        TaxClassCode = Trim(taxClassCode);
    }

    /// <summary>
    /// Adds a variant to this item, marking it as an item that now sells only through its variants.
    /// </summary>
    /// <param name="sku">The variant's unique SKU within the tenant, upper-cased.</param>
    /// <param name="attributes">The variant's attribute pairs, for example <c>Size: M</c>.</param>
    /// <remarks>
    /// The caller (the command handler) must confirm before calling this that the item carries no
    /// barcode of its own — <see cref="CatalogRuleException.ItemHasDirectBarcodesCannotAddVariant"/> is
    /// the refusal for that case, thrown from the handler because the check needs the barcode
    /// repository, which this aggregate has no access to.
    /// </remarks>
    public ItemVariant AddVariant(string sku, IReadOnlyList<VariantAttribute> attributes)
    {
        ItemVariant variant = ItemVariant.Create(TenantId, Id, sku, attributes);
        HasVariants = true;
        return variant;
    }

    /// <summary>Retires the item from sale and receipt. History is retained; nothing is deleted (§7 rule 8).</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Brings a retired item back into use.</summary>
    public void Activate() => IsActive = true;

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>What kind of thing an <see cref="Item"/> is.</summary>
public enum ItemType
{
    /// <summary>A physical good tracked through Stage 08's stock ledger.</summary>
    Stock = 0,

    /// <summary>Labour or a non-physical offering. Never carries stock.</summary>
    Service = 1,

    /// <summary>Something sold or received without stock being tracked, for example a gift-wrap fee.</summary>
    NonStock = 2,
}
