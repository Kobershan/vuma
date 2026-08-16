using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales;

/// <summary>
/// A named set of prices — the shelf price, the wholesale price, the staff price.
/// </summary>
/// <remarks>
/// <para>
/// ADR-072 left <c>SaleLine.UnitPrice</c> supplied by the caller because there was no price list on
/// <c>main</c> to read from. This is that list. It does not change the sale line's shape: the till
/// resolves a price through this entity and passes the answer into the unchanged command, which is why
/// a weighed, open-price or manually reduced line still works exactly as it did.
/// </para>
/// <para>
/// <see cref="PricesIncludeTax"/> is on the list, not the line, because it is a property of how a list
/// was authored: a South African shelf price is quoted inclusive and a wholesale list is not. Storing
/// it here means the resolver can hand <c>ITaxCalculator</c> a stated amount and let the matched tax
/// rule decide the split, rather than every caller having to know which kind of list it read.
/// </para>
/// <para>
/// <see cref="Entity.StoreId"/> is optional: a tenant-wide list is the normal case, and a store-scoped
/// list is how one branch runs different prices. The scoped list wins — see <see cref="Priority"/>.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class PriceList : Entity
{
    private readonly List<PriceListLine> _lines = [];

    private PriceList(
        Guid tenantId,
        Guid? storeId,
        string code,
        string name,
        string currency,
        PriceListKind kind,
        bool pricesIncludeTax,
        int priority,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo)
        : base(tenantId, storeId)
    {
        Code = code;
        Name = name;
        Currency = currency;
        Kind = kind;
        PricesIncludeTax = pricesIncludeTax;
        Priority = priority;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private PriceList()
    {
    }

    /// <summary>The list's unique code within the tenant, upper-cased, like <c>Store.Code</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The list's name, as a manager would recognise it.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The currency every line on this list is denominated in.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>What kind of list this is.</summary>
    public PriceListKind Kind { get; private set; }

    /// <summary>
    /// True when the prices on this list already include tax, as a South African shelf price does.
    /// </summary>
    public bool PricesIncludeTax { get; private set; }

    /// <summary>
    /// Which list wins when more than one prices the same item. Higher wins; ties break on
    /// <see cref="Code"/> so that resolution is deterministic rather than dependent on row order.
    /// </summary>
    public int Priority { get; private set; }

    /// <summary>The first day this list may be used.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>The last day this list may be used, inclusive, or <c>null</c> for open-ended.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>True while the list may be resolved against. Deactivate, not delete (§7 rule 8).</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>The prices on this list.</summary>
    public IReadOnlyList<PriceListLine> Lines => _lines;

    /// <summary>Creates a price list.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store this list is scoped to, or <c>null</c> for tenant-wide.</param>
    /// <param name="code">The list code, upper-cased.</param>
    /// <param name="name">The list's name.</param>
    /// <param name="currency">The currency the list is denominated in.</param>
    /// <param name="kind">What kind of list this is.</param>
    /// <param name="pricesIncludeTax">True when the prices already include tax.</param>
    /// <param name="priority">Which list wins when several price the same item. Higher wins.</param>
    /// <param name="effectiveFrom">The first day it may be used.</param>
    /// <param name="effectiveTo">The last day it may be used, or <c>null</c>.</param>
    /// <exception cref="SalesRuleException">The effective window ends before it begins.</exception>
    public static PriceList Create(
        Guid tenantId,
        Guid? storeId,
        string code,
        string name,
        string currency,
        PriceListKind kind,
        bool pricesIncludeTax,
        int priority,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A price list must belong to a tenant.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        EnsureWindowIsSane(effectiveFrom, effectiveTo);

        return new PriceList(
            tenantId,
            storeId,
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            currency.Trim().ToUpperInvariant(),
            kind,
            pricesIncludeTax,
            priority,
            effectiveFrom,
            effectiveTo);
    }

    /// <summary>True when this list may be resolved against on the given day.</summary>
    /// <param name="on">The day, normally the document date.</param>
    public bool IsEffectiveOn(DateOnly on)
        => IsActive && on >= EffectiveFrom && (EffectiveTo is null || on <= EffectiveTo);

    /// <summary>Renames the list and moves its effective window.</summary>
    /// <param name="name">The new name.</param>
    /// <param name="priority">The new priority.</param>
    /// <param name="effectiveFrom">The new first day.</param>
    /// <param name="effectiveTo">The new last day, or <c>null</c>.</param>
    /// <exception cref="SalesRuleException">The effective window ends before it begins.</exception>
    public void Amend(string name, int priority, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureWindowIsSane(effectiveFrom, effectiveTo);

        Name = name.Trim();
        Priority = priority;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    /// <summary>Adds a price to the list.</summary>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="unitPrice">What one unit sells for.</param>
    /// <param name="minimumQuantity">The quantity this price starts applying at — a quantity break.</param>
    /// <returns>The new line.</returns>
    /// <exception cref="SalesRuleException">
    /// The line names both or neither of item and variant, is priced in another currency, is negative,
    /// or the minimum quantity is not positive.
    /// </exception>
    /// <exception cref="SalesConflictException">That item is already priced at that break.</exception>
    public PriceListLine AddLine(
        Guid? itemId, Guid? itemVariantId, Money unitPrice, decimal minimumQuantity)
    {
        if (!string.Equals(unitPrice.Currency, Currency, StringComparison.Ordinal))
        {
            throw SalesRuleException.CurrencyMismatch(Currency, unitPrice.Currency);
        }

        bool duplicate = _lines.Exists(line =>
            line.ItemId == itemId
            && line.ItemVariantId == itemVariantId
            && line.MinimumQuantity == minimumQuantity);

        if (duplicate)
        {
            throw SalesConflictException.DuplicatePriceListLine();
        }

        PriceListLine created = PriceListLine.Create(
            TenantId, StoreId, Id, itemId, itemVariantId, unitPrice, minimumQuantity);

        _lines.Add(created);
        return created;
    }

    /// <summary>
    /// Finds the price that applies to an item at a quantity — the highest quantity break at or below
    /// what is being sold.
    /// </summary>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="quantity">How many are being sold.</param>
    /// <returns>The winning line, or <c>null</c> when this list does not price that item.</returns>
    /// <remarks>
    /// A quantity break is "this price from N upwards", so the winner is the largest
    /// <see cref="PriceListLine.MinimumQuantity"/> that does not exceed the quantity. Ties cannot
    /// happen — <see cref="AddLine"/> refuses a duplicate break for the same item.
    /// </remarks>
    public PriceListLine? FindPrice(Guid? itemId, Guid? itemVariantId, decimal quantity)
        => _lines
            .Where(line =>
                line.ItemId == itemId
                && line.ItemVariantId == itemVariantId
                && line.MinimumQuantity <= quantity)
            .OrderByDescending(line => line.MinimumQuantity)
            .FirstOrDefault();

    /// <summary>Retires the list from resolution. History is retained; nothing is deleted.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Brings a retired list back into use.</summary>
    public void Activate() => IsActive = true;

    private static void EnsureWindowIsSane(DateOnly from, DateOnly? to)
    {
        if (to is not null && to < from)
        {
            throw SalesRuleException.EffectiveWindowInverted();
        }
    }
}

/// <summary>What kind of prices a <see cref="PriceList"/> holds.</summary>
public enum PriceListKind
{
    /// <summary>The shelf price a walk-in customer pays.</summary>
    Retail = 0,

    /// <summary>A trade price, normally quoted excluding tax.</summary>
    Wholesale = 1,

    /// <summary>A staff price.</summary>
    Staff = 2,
}
