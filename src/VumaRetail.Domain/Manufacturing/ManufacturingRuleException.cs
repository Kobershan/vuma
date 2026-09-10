using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Manufacturing;

/// <summary>Thrown when a manufacturing definition breaks a Stage 16 invariant.</summary>
public sealed class ManufacturingRuleException(string code, string message) : DomainException(code, message)
{
    /// <summary>The definition cannot contain its own finished item.</summary>
    public static ManufacturingRuleException CannotContainItself() => new("BOM_SELF_REFERENCE", "A BOM cannot contain itself.");

    /// <summary>A component quantity must be positive.</summary>
    public static ManufacturingRuleException PositiveQuantityRequired() => new("BOM_POSITIVE_QUANTITY", "A BOM component quantity must be positive.");

    /// <summary>The scrap percentage is outside the supported range.</summary>
    public static ManufacturingRuleException InvalidScrap(decimal value) => new("BOM_INVALID_SCRAP", $"Scrap percentage {value} must be at least zero and below 100.");

    /// <summary>A published BOM cannot be changed.</summary>
    public static ManufacturingRuleException InvalidTransition(BillOfMaterialsStatus from, BillOfMaterialsStatus to)
        => new("BOM_INVALID_TRANSITION", $"A BOM cannot move from {from} to {to}.");

    /// <summary>An empty definition cannot become active.</summary>
    public static ManufacturingRuleException EmptyBomCannotBePublished() => new("BOM_EMPTY", "A BOM must contain at least one component before publication.");

    /// <summary>The operation requires a published definition.</summary>
    public static ManufacturingRuleException PublishedDefinitionRequired() => new("BOM_NOT_PUBLISHED", "Only a published BOM can be exploded.");

    /// <summary>A recursive BOM graph contains a cycle.</summary>
    public static ManufacturingRuleException CycleDetected(object key) => new("BOM_CYCLE", $"The BOM graph contains a cycle at {key}.");

    /// <summary>A leaf has no costing input.</summary>
    public static ManufacturingRuleException MissingCost(object key) => new("BOM_COST_MISSING", $"No unit cost was supplied for component {key}.");

    /// <summary>All costs in one explosion must use one currency.</summary>
    public static ManufacturingRuleException MixedCostCurrency(string expected, string actual)
        => new("BOM_MIXED_CURRENCY", $"BOM costs must use {expected}; received {actual}.");

    /// <summary>A definition version already exists.</summary>
    public static ManufacturingRuleException DuplicateVersion(Guid itemId, int version)
        => new("BOM_DUPLICATE_VERSION", $"BOM version {version} already exists for item {itemId}.");

    /// <summary>A requested definition does not exist in the tenant.</summary>
    public static ManufacturingRuleException NotFound(Guid id)
        => new("BOM_NOT_FOUND", $"BOM {id} was not found.");
}
