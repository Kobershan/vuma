using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Manufacturing;

/// <summary>A versioned bill of materials for one manufactured or assembled sellable.</summary>
/// <remarks>Stage 16 owns definition and costing inputs; production execution belongs to Stage 17.</remarks>
public sealed class BillOfMaterials : Entity
{
    private readonly List<BillOfMaterialsLine> _lines = [];

    private BillOfMaterials(Guid tenantId, Guid finishedItemId, int version, string name)
        : base(tenantId)
    {
        FinishedItemId = finishedItemId;
        Version = version;
        Name = name;
        Status = BillOfMaterialsStatus.Draft;
    }

    private BillOfMaterials()
    {
    }

    /// <summary>The item produced by this BOM. Variant-specific BOMs are represented by the variant id.</summary>
    public Guid FinishedItemId { get; private set; }

    /// <summary>The variant produced by this BOM, or <c>null</c> for an item-level BOM.</summary>
    public Guid? FinishedVariantId { get; private set; }

    /// <summary>The version number of this definition for its finished item.</summary>
    public int Version { get; private set; }

    /// <summary>The human-readable name of the definition.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The lifecycle state of this definition.</summary>
    public BillOfMaterialsStatus Status { get; private set; }

    /// <summary>The ordered component lines.</summary>
    public IReadOnlyList<BillOfMaterialsLine> Lines => _lines;

    /// <summary>Creates a draft BOM.</summary>
    public static BillOfMaterials Create(Guid tenantId, Guid finishedItemId, int version, string name, Guid? finishedVariantId = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A BOM must belong to a tenant.", nameof(tenantId));
        }
        if (finishedItemId == Guid.Empty)
        {
            throw new ArgumentException("A BOM must name a finished item.", nameof(finishedItemId));
        }
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "A BOM version must be positive.");
        }
        if (finishedVariantId == finishedItemId)
        {
            throw new ArgumentException("The finished variant cannot equal the item.", nameof(finishedVariantId));
        }

        return new BillOfMaterials(tenantId, finishedItemId, version, Require(name, nameof(name)))
        {
            FinishedVariantId = finishedVariantId,
        };
    }

    /// <summary>Adds a component line while this definition is still a draft.</summary>
    public BillOfMaterialsLine AddLine(
        Guid componentItemId,
        Quantity quantity,
        decimal scrapPercent = 0m,
        string? alternateGroup = null,
        Guid? componentVariantId = null)
    {
        EnsureDraft();
        if (componentItemId == Guid.Empty)
        {
            throw new ArgumentException("A BOM component must name an item.", nameof(componentItemId));
        }
        if (componentItemId == FinishedItemId && componentVariantId == FinishedVariantId)
        {
            throw ManufacturingRuleException.CannotContainItself();
        }
        if (quantity.Value <= 0m)
        {
            throw ManufacturingRuleException.PositiveQuantityRequired();
        }
        if (scrapPercent is < 0m or >= 100m)
        {
            throw ManufacturingRuleException.InvalidScrap(scrapPercent);
        }

        BillOfMaterialsLine line = new(componentItemId, componentVariantId, quantity, scrapPercent, alternateGroup);
        _lines.Add(line);
        return line;
    }

    /// <summary>Publishes the definition for planning and costing.</summary>
    public void Publish()
    {
        EnsureDraft();
        if (_lines.Count == 0)
        {
            throw ManufacturingRuleException.EmptyBomCannotBePublished();
        }
        Status = BillOfMaterialsStatus.Published;
    }

    /// <summary>Retires a published definition without deleting its history.</summary>
    public void Retire()
    {
        if (Status is not BillOfMaterialsStatus.Published)
        {
            throw ManufacturingRuleException.InvalidTransition(Status, BillOfMaterialsStatus.Retired);
        }
        Status = BillOfMaterialsStatus.Retired;
    }

    private void EnsureDraft()
    {
        if (Status is not BillOfMaterialsStatus.Draft)
        {
            throw ManufacturingRuleException.InvalidTransition(Status, BillOfMaterialsStatus.Draft);
        }
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();
}

/// <summary>One component required by a BOM.</summary>
public sealed record BillOfMaterialsLine(
    Guid ComponentItemId,
    Guid? ComponentVariantId,
    Quantity Quantity,
    decimal ScrapPercent,
    string? AlternateGroup);

/// <summary>Lifecycle of a BOM definition.</summary>
public enum BillOfMaterialsStatus
{
    /// <summary>The definition is editable and not available to production.</summary>
    Draft = 0,

    /// <summary>The definition is immutable and available to planning.</summary>
    Published = 1,

    /// <summary>The definition is historical and no longer active.</summary>
    Retired = 2,
}
