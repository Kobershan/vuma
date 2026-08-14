using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Catalog;

/// <summary>
/// One sellable variation of an <see cref="Item"/> — a size, a colour, a combination of both.
/// </summary>
/// <remarks>
/// Created only through <see cref="Item.AddVariant"/>, never directly, so an <see cref="Item"/> is
/// always the one place that knows it has gained its first variant and needs to record
/// <see cref="Item.HasVariants"/>.
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class ItemVariant : Entity
{
    private List<VariantAttribute> _attributes = [];

    private ItemVariant(Guid tenantId, Guid itemId, string sku)
        : base(tenantId)
    {
        ItemId = itemId;
        Sku = sku;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ItemVariant()
    {
    }

    /// <summary>The parent item. Same schema, so this is a plain reference rather than a navigation.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>The variant's unique code within the tenant, upper-cased.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>
    /// The variant's attribute pairs, in the order they were set — for example <c>Size: M</c>,
    /// <c>Colour: Red</c>.
    /// </summary>
    /// <remarks>
    /// A value object stored as a single ordered list rather than a child table (ADR — see
    /// <c>docs/DECISIONS.md</c>): no retailer needs a third dimension routinely, and a fourth is a data
    /// edit rather than a migration that has to touch every existing variant row.
    /// </remarks>
    public IReadOnlyList<VariantAttribute> Attributes => _attributes;

    /// <summary>True while the variant may be sold or received.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Creates a variant. Called only by <see cref="Item.AddVariant"/>.</summary>
    /// <param name="tenantId">The owning tenant — the item's own.</param>
    /// <param name="itemId">The parent item.</param>
    /// <param name="sku">The variant's SKU, upper-cased.</param>
    /// <param name="attributes">The attribute pairs.</param>
    internal static ItemVariant Create(
        Guid tenantId,
        Guid itemId,
        string sku,
        IReadOnlyList<VariantAttribute> attributes)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A variant must belong to a tenant.", nameof(tenantId));
        }

        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("A variant must belong to an item.", nameof(itemId));
        }

        ItemVariant variant = new(tenantId, itemId, Require(sku, nameof(sku)).ToUpperInvariant());
        variant.ReplaceAttributes(attributes);

        return variant;
    }

    /// <summary>Replaces the variant's attribute pairs wholesale.</summary>
    /// <param name="attributes">The new attribute pairs, in order.</param>
    public void ReplaceAttributes(IReadOnlyList<VariantAttribute> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        _attributes = [.. attributes];
    }

    /// <summary>Retires the variant from sale and receipt. History is retained; nothing is deleted.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Brings a retired variant back into use.</summary>
    public void Activate() => IsActive = true;

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();
}

/// <summary>
/// One named attribute of an <see cref="ItemVariant"/>, for example <c>("Size", "M")</c>.
/// </summary>
/// <param name="Name">The attribute's name, for example <c>Size</c>.</param>
/// <param name="Value">The attribute's value, for example <c>M</c>.</param>
public sealed record VariantAttribute(string Name, string Value)
{
    /// <summary>Builds an attribute pair, trimming both fields.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="value">The attribute's value.</param>
    /// <exception cref="ArgumentException">Either field is blank.</exception>
    public static VariantAttribute Create(string name, string value)
        => new(Require(name, nameof(name)), Require(value, nameof(value)));

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();
}
