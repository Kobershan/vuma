using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Procurement;

/// <summary>One thing the RFQ is asking suppliers to price.</summary>
/// <remarks>
/// No cost of any kind. That is the whole difference between this and a purchase order line: an RFQ
/// line is a question, and putting a number on the question is how a buyer accidentally anchors the
/// answer.
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class RfqLine : Entity
{
    private RfqLine(
        Guid tenantId,
        Guid? storeId,
        Guid rfqId,
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        Quantity quantity,
        string? specification,
        Guid? purchaseRequisitionLineId)
        : base(tenantId, storeId)
    {
        RfqId = rfqId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Description = description;
        Quantity = quantity;
        Specification = specification;
        PurchaseRequisitionLineId = purchaseRequisitionLineId;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private RfqLine()
    {
    }

    /// <summary>The RFQ this line belongs to.</summary>
    public Guid RfqId { get; private set; }

    /// <summary>The item, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>What is wanted.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>How much. Always positive.</summary>
    public Quantity Quantity { get; private set; }

    /// <summary>Any further requirement — grade, pack size, delivery split — or <c>null</c>.</summary>
    public string? Specification { get; private set; }

    /// <summary>The requisition line this satisfies, or <c>null</c> if the RFQ was raised directly.</summary>
    public Guid? PurchaseRequisitionLineId { get; private set; }

    /// <summary>Builds an RFQ line. Prefer <see cref="Rfq.AddLine"/>, which enforces the document's rules.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store sourcing.</param>
    /// <param name="rfqId">The RFQ.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="description">What is wanted.</param>
    /// <param name="quantity">How much. Positive.</param>
    /// <param name="specification">Any further requirement, or <c>null</c>.</param>
    /// <param name="purchaseRequisitionLineId">The requisition line this satisfies, or <c>null</c>.</param>
    /// <exception cref="ProcurementRuleException">The line names the wrong number of SKUs, or the quantity is not positive.</exception>
    public static RfqLine For(
        Guid tenantId,
        Guid? storeId,
        Guid rfqId,
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        Quantity quantity,
        string? specification,
        Guid? purchaseRequisitionLineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        ProcurementLineRules.RequireExactlyOneStockKeepingUnit(itemId, itemVariantId);
        ProcurementLineRules.RequirePositive(quantity);

        return new RfqLine(
            tenantId, storeId, rfqId, itemId, itemVariantId, description.Trim(), quantity,
            string.IsNullOrWhiteSpace(specification) ? null : specification.Trim(),
            purchaseRequisitionLineId);
    }
}
