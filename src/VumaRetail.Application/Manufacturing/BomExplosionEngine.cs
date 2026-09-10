using VumaRetail.Domain.Manufacturing;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Manufacturing;

/// <summary>Identifies an item or one of its variants in a BOM graph.</summary>
public readonly record struct BomComponentKey(Guid ItemId, Guid? VariantId)
{
    /// <summary>Creates a key and rejects an empty item identity.</summary>
    public static BomComponentKey Create(Guid itemId, Guid? variantId = null)
        => itemId == Guid.Empty
            ? throw new ArgumentException("A BOM key must name an item.", nameof(itemId))
            : new(itemId, variantId);
}

/// <summary>The quantity and rolled-up cost of one exploded leaf component.</summary>
public sealed record ExplodedBomComponent(BomComponentKey Component, Quantity Quantity, Money ExtendedCost);

/// <summary>The complete result of exploding a BOM for a requested output quantity.</summary>
public sealed record BomExplosionResult(IReadOnlyList<ExplodedBomComponent> Components, Money TotalCost);

/// <summary>
/// Explodes published, multi-level BOM definitions and rolls their leaf costs up to the finished item.
/// </summary>
public sealed class BomExplosionEngine
{
    /// <summary>
    /// Explodes a root BOM. A group with no selected alternate uses its first line in definition order;
    /// callers can select a different line explicitly by key.
    /// </summary>
    /// <param name="root">The published BOM to explode.</param>
    /// <param name="outputQuantity">The requested finished quantity.</param>
    /// <param name="definitions">Published nested definitions, keyed by finished item and variant.</param>
    /// <param name="unitCosts">Leaf unit costs, keyed by component item and variant.</param>
    /// <param name="currency">The expected costing currency.</param>
    /// <param name="selectedAlternates">Optional component keys selected for alternate groups.</param>
    /// <exception cref="ManufacturingRuleException">The graph contains a cycle or a cost is missing.</exception>
    public BomExplosionResult Explode(
        BillOfMaterials root,
        Quantity outputQuantity,
        IReadOnlyDictionary<BomComponentKey, BillOfMaterials> definitions,
        IReadOnlyDictionary<BomComponentKey, Money> unitCosts,
        string currency,
        IReadOnlySet<BomComponentKey>? selectedAlternates = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(unitCosts);
        ArgumentNullException.ThrowIfNull(currency);

        if (root.Status is not BillOfMaterialsStatus.Published)
        {
            throw ManufacturingRuleException.PublishedDefinitionRequired();
        }
        if (outputQuantity.Value <= 0m)
        {
            throw ManufacturingRuleException.PositiveQuantityRequired();
        }

        Money total = Money.Zero(currency);
        List<ExplodedBomComponent> components = [];
        HashSet<BomComponentKey> path = [];
        BomComponentKey rootKey = BomComponentKey.Create(root.FinishedItemId, root.FinishedVariantId);

        Expand(root, outputQuantity.Value, definitions, unitCosts, currency, selectedAlternates ?? new HashSet<BomComponentKey>(), path, components, ref total, rootKey);
        return new BomExplosionResult(components, total);
    }

    private static void Expand(
        BillOfMaterials bom,
        decimal factor,
        IReadOnlyDictionary<BomComponentKey, BillOfMaterials> definitions,
        IReadOnlyDictionary<BomComponentKey, Money> unitCosts,
        string currency,
        IReadOnlySet<BomComponentKey> selectedAlternates,
        HashSet<BomComponentKey> path,
        List<ExplodedBomComponent> components,
        ref Money total,
        BomComponentKey bomKey)
    {
        if (!path.Add(bomKey))
        {
            throw ManufacturingRuleException.CycleDetected(bomKey);
        }

        foreach (IGrouping<string?, BillOfMaterialsLine> group in bom.Lines.GroupBy(line => line.AlternateGroup))
        {
            IEnumerable<BillOfMaterialsLine> candidates = group.Key is null
                ? group
                : [.. group.Where(line => selectedAlternates.Contains(BomComponentKey.Create(line.ComponentItemId, line.ComponentVariantId)))];
            BillOfMaterialsLine? line = candidates.FirstOrDefault() ?? group.FirstOrDefault();

            if (line is null)
            {
                continue;
            }

            BomComponentKey component = BomComponentKey.Create(line.ComponentItemId, line.ComponentVariantId);
            decimal required = factor * line.Quantity.Value / (1m - line.ScrapPercent / 100m);

            if (definitions.TryGetValue(component, out BillOfMaterials? nested))
            {
                if (nested.Status is not BillOfMaterialsStatus.Published)
                {
                    throw ManufacturingRuleException.PublishedDefinitionRequired();
                }

                Expand(nested, required, definitions, unitCosts, currency, selectedAlternates, path, components, ref total, component);
                continue;
            }

            if (!unitCosts.TryGetValue(component, out Money unitCost))
            {
                throw ManufacturingRuleException.MissingCost(component);
            }
            if (!string.Equals(unitCost.Currency, currency.Trim().ToUpperInvariant(), StringComparison.Ordinal))
            {
                throw ManufacturingRuleException.MixedCostCurrency(currency, unitCost.Currency);
            }

            Money extended = new Quantity(required, line.Quantity.UnitOfMeasure).Extend(unitCost);
            components.Add(new ExplodedBomComponent(component, new Quantity(required, line.Quantity.UnitOfMeasure), extended));
            total += extended;
        }

        path.Remove(bomKey);
    }
}
