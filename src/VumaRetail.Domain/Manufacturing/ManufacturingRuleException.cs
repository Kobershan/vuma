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
}
