using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Orders;

/// <summary>Something order-owned was asked for that does not exist, or is not this tenant's.</summary>
/// <param name="what">What was being looked for, for example <c>sales order</c>.</param>
/// <param name="id">The identifier that found nothing.</param>
public sealed class OrdersNotFoundException(string what, Guid id)
    : DomainException("ORDERS_NOT_FOUND", $"No {what} with id {id}.", DomainProblemKind.NotFound);

/// <summary>An orders module business rule was broken.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What the rule says.</param>
public sealed class OrdersRuleException(string code, string message) : DomainException(code, message)
{
    /// <summary>A line named neither an item nor a variant, or named both.</summary>
    public static OrdersRuleException ExactlyOneItemOrVariantRequired()
        => new(
            "ORDERS_EXACTLY_ONE_ITEM_OR_VARIANT",
            "An order line identifies exactly one of an item (when it has no variants) or a variant — "
            + "never both, never neither.");

    /// <summary>A quantity was zero or negative where a positive amount is required.</summary>
    public static OrdersRuleException QuantityMustBePositive()
        => new("ORDERS_QUANTITY_MUST_BE_POSITIVE", "The quantity must be greater than zero.");

    /// <summary>A delivery order was raised with no delivery address.</summary>
    public static OrdersRuleException DeliveryRequiresAddress()
        => new("ORDERS_DELIVERY_REQUIRES_ADDRESS", "A delivery order must carry a delivery address.");

    /// <summary>An order was confirmed, or a return completed, with no lines on it.</summary>
    public static OrdersRuleException OrderHasNoLines()
        => new("ORDERS_ORDER_HAS_NO_LINES", "An order must have at least one line before it can be confirmed.");

    /// <summary>An operation needs the order/return to be in a different status than it is.</summary>
    public static OrdersRuleException UnexpectedOrderStatus(SalesOrderStatus actual)
        => new("ORDERS_UNEXPECTED_ORDER_STATUS", $"This operation cannot be performed while the order is {actual}.");

    /// <summary>An order was confirmed that is not a draft.</summary>
    public static OrdersRuleException OrderNotDraft(SalesOrderStatus actual)
        => new("ORDERS_ORDER_NOT_DRAFT", $"Only a draft order can be confirmed or have lines added; this order is {actual}.");

    /// <summary>A return was asked to add a line, or complete, when it is not open.</summary>
    public static OrdersRuleException UnexpectedReturnStatus(SalesOrderReturnStatus actual)
        => new("ORDERS_UNEXPECTED_RETURN_STATUS", $"This operation cannot be performed while the return is {actual}.");

    /// <summary>A line's cancellation was requested for a quantity that has already shipped.</summary>
    public static OrdersRuleException CannotCancelFulfilledLine()
        => new(
            "ORDERS_CANNOT_CANCEL_FULFILLED_LINE",
            "This line has already shipped in whole or in part. Raise a SalesOrderReturn for what shipped "
            + "instead of cancelling the line.");

    /// <summary>A whole-order cancel was requested against an order that has already shipped something.</summary>
    public static OrdersRuleException CannotCancelFulfilledOrder()
        => new(
            "ORDERS_CANNOT_CANCEL_FULFILLED_ORDER",
            "This order has already shipped one or more lines in whole or in part. Cancel the remaining "
            + "open lines individually, or raise a return for what shipped.");

    /// <summary>A return line asked for more than the original line's fulfilled quantity (business rule 6).</summary>
    /// <param name="fulfilled">How much of the original line was actually fulfilled.</param>
    /// <param name="previouslyReturned">How much of that had already come back on earlier return documents.</param>
    /// <param name="requested">How much this line is asking for.</param>
    public static OrdersRuleException ReturnExceedsFulfilledQuantity(decimal fulfilled, decimal previouslyReturned, decimal requested)
        => new(
            "ORDERS_RETURN_EXCEEDS_FULFILLED_QUANTITY",
            $"Only {fulfilled - previouslyReturned} of this line remains available to return "
            + $"({fulfilled} fulfilled, {previouslyReturned} already returned); {requested} was requested.");

    /// <summary>The same original order line appears twice on one return document.</summary>
    public static OrdersRuleException DuplicateReturnLine()
        => new("ORDERS_DUPLICATE_RETURN_LINE", "This order line is already on this return document.");

    /// <summary>A return was completed, or asked to add a line, with no lines on it.</summary>
    public static OrdersRuleException ReturnHasNoLines()
        => new("ORDERS_RETURN_HAS_NO_LINES", "A return must have at least one line before it can be completed.");

    /// <summary>An order was asked to complete while some active line has not yet fully shipped.</summary>
    public static OrdersRuleException OrderNotFullyAccountedFor()
        => new(
            "ORDERS_NOT_FULLY_ACCOUNTED_FOR",
            "This order cannot complete: at least one active line has neither shipped in full nor been "
            + "cancelled. Refresh fulfilment first, or wait for the remaining stock to ship.");
}
