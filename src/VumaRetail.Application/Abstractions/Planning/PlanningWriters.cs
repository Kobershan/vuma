using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Abstractions.Planning;

/// <summary>One line on a requisition the planning module raises.</summary>
/// <param name="ItemId">The item, or <c>null</c> for a variant.</param>
/// <param name="ItemVariantId">The variant, or <c>null</c> for an item.</param>
/// <param name="Description">What is needed, in words.</param>
/// <param name="Quantity">How much. Positive.</param>
/// <param name="Uom">The unit of measure.</param>
/// <param name="EstimatedUnitCost">The planner's cost estimate, or <c>null</c>.</param>
public sealed record PlannedRequisitionLine(
    Guid? ItemId,
    Guid? ItemVariantId,
    string Description,
    decimal Quantity,
    string Uom,
    Money? EstimatedUnitCost);

/// <summary>
/// Raises supplier-free purchase requisitions through Stage 12's commands. Planning never
/// constructs a <c>PurchaseRequisition</c> itself.
/// </summary>
public interface IPlanningProcurementWriter
{
    /// <summary>Raises a draft requisition with its lines. Returns the requisition id.</summary>
    Task<Guid> RaiseRequisitionAsync(
        Guid? locationId,
        DateOnly requiredBy,
        string justification,
        IReadOnlyList<PlannedRequisitionLine> lines,
        CancellationToken cancellationToken = default);
}

/// <summary>Moves stock through Stage 08's transfer command. Planning never posts ledger entries itself.</summary>
public interface IPlanningTransferWriter
{
    /// <summary>Transfers between two locations. Returns the transfer document id.</summary>
    Task<Guid> TransferAsync(
        Guid sourceLocationId,
        Guid destinationLocationId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantity,
        string uom,
        string? note,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates and retires SKU promotions through Stage 10's commands. Planning never writes to
/// <c>sales.promotions</c> directly — Stage 10 remains the pricing authority.
/// </summary>
public interface IPlanningPromotionWriter
{
    /// <summary>Creates, lines and activates a percentage-off promotion for one SKU. Returns the promotion id.</summary>
    Task<Guid> CreateSkuPromotionAsync(
        string code,
        string name,
        decimal discountPercent,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        Guid? storeId,
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default);

    /// <summary>Retires a promotion planning created.</summary>
    Task DeactivatePromotionAsync(Guid promotionId, CancellationToken cancellationToken = default);
}

/// <summary>One live price snapshot for a SKU.</summary>
/// <param name="Price">The resolved unit price.</param>
/// <param name="Currency">Its currency.</param>
/// <param name="AverageCost">The weighted-average cost, or <c>null</c> when the SKU never moved here.</param>
public sealed record PriceSnapshot(decimal Price, string Currency, decimal? AverageCost);

/// <summary>Reads live prices and costs for markdown authoring. Returns <c>null</c> when unpriced.</summary>
public interface IPlanningPriceReader
{
    /// <summary>Resolves the live price off the winning price list plus average cost off the balance.</summary>
    Task<PriceSnapshot?> TryReadAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        Guid? storeId,
        DateOnly onDate,
        CancellationToken cancellationToken = default);
}
