using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Inventory;

/// <summary>Something inventory-owned was asked for that does not exist, or is not this tenant's.</summary>/// <param name="what">What was being looked for, for example <c>stock location</c>.</param>
/// <param name="id">The identifier that found nothing.</param>
public sealed class InventoryNotFoundException(string what, Guid id)
    : DomainException("INVENTORY_NOT_FOUND", $"No {what} with id {id}.", DomainProblemKind.NotFound);

/// <summary>Something inventory-owned must be unique and is not.</summary>
/// <param name="code">The stable machine-readable code for the specific collision.</param>
/// <param name="message">What collided, in words.</param>
public sealed class InventoryConflictException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Conflict)
{
    /// <summary>A stock location code already used in this tenant.</summary>
    /// <param name="code">The code.</param>
    public static InventoryConflictException LocationCode(string code)
        => new("INVENTORY_LOCATION_CODE_TAKEN", $"Location code '{code}' is already used in this tenant.");
}

/// <summary>An inventory business rule was broken.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What the rule says.</param>
public sealed class InventoryRuleException(string code, string message) : DomainException(code, message)
{
    /// <summary>A stock movement named neither an item nor a variant, or named both.</summary>
    public static InventoryRuleException ExactlyOneItemOrVariantRequired()
        => new(
            "INVENTORY_EXACTLY_ONE_ITEM_OR_VARIANT",
            "A stock movement identifies exactly one of an item (when it has no variants) or a variant "
            + "— never both, never neither.");

    /// <summary>A ledger entry was asked to record a zero quantity, which is not a movement at all.</summary>
    public static InventoryRuleException QuantityMustBeNonZero()
        => new("INVENTORY_QUANTITY_MUST_BE_NON_ZERO", "A stock movement must move a non-zero quantity.");

    /// <summary>A receipt, transfer or count quantity was zero or negative where a positive amount is required.</summary>
    public static InventoryRuleException QuantityMustBePositive()
        => new("INVENTORY_QUANTITY_MUST_BE_POSITIVE", "The quantity must be greater than zero.");

    /// <summary>An adjustment carried no reason code — CLAUDE.md §7 rule 6: a correction is a documented ledger entry.</summary>
    public static InventoryRuleException AdjustmentRequiresReasonCode()
        => new(
            "INVENTORY_ADJUSTMENT_REQUIRES_REASON",
            "An adjustment must carry a documented reason code.");

    /// <summary>A transfer, stocktake or sale movement carried no reference id to correlate it to its document.</summary>
    public static InventoryRuleException ReferenceIdRequired()
        => new(
            "INVENTORY_REFERENCE_ID_REQUIRED",
            "A transfer, stocktake or sale movement must carry the id of the document it belongs to.");

    /// <summary>A movement's quantity is in a different unit of measure than the balance it is posting against.</summary>
    /// <param name="expected">The unit of measure the balance is already held in.</param>
    /// <param name="actual">The unit of measure the movement tried to post in.</param>
    public static InventoryRuleException UnitOfMeasureMismatch(string expected, string actual)
        => new(
            "INVENTORY_UOM_MISMATCH",
            $"This balance is held in '{expected}'; '{actual}' cannot be posted against it directly. "
            + "Convert through the unit-of-measure catalogue first.");

    /// <summary>A movement's cost is in a different currency than the balance it is posting against.</summary>
    /// <param name="expected">The currency the balance is already valued in.</param>
    /// <param name="actual">The currency the movement tried to post in.</param>
    public static InventoryRuleException CurrencyMismatch(string expected, string actual)
        => new(
            "INVENTORY_CURRENCY_MISMATCH",
            $"This balance is valued in {expected}; {actual} cannot be posted against it directly.");

    /// <summary>
    /// A sale, issue, transfer-out or negative adjustment/variance would take a location's on-hand
    /// quantity below zero.
    /// </summary>
    /// <param name="available">What is on hand.</param>
    /// <param name="requested">What was asked to be relieved.</param>
    public static InventoryRuleException InsufficientStock(Quantity available, Quantity requested)
        => new(
            "INVENTORY_INSUFFICIENT_STOCK",
            $"Only {available} is on hand; {requested} was requested. Stock cannot go negative through "
            + "this path — post a stocktake variance if a physical count disagrees with the system.");

    /// <summary>A receipt-type posting with no existing balance carried no unit cost to open one with.</summary>
    public static InventoryRuleException UnitCostRequiredToOpenBalance()
        => new(
            "INVENTORY_UNIT_COST_REQUIRED",
            "The first receipt against a new item/location combination must carry a unit cost — there is "
            + "no existing average to value it at.");

    /// <summary>A transfer named the same location as both source and destination.</summary>
    public static InventoryRuleException TransferLocationsMustDiffer()
        => new(
            "INVENTORY_TRANSFER_SAME_LOCATION",
            "A transfer's source and destination locations must be different.");

    /// <summary>A stocktake session was asked to accept a count or be finalized twice.</summary>
    public static InventoryRuleException StocktakeAlreadyFinalized()
        => new(
            "INVENTORY_STOCKTAKE_ALREADY_FINALIZED",
            "This stocktake session is already finalized. A correction is a new adjustment, not an edit "
            + "to a finalized count (CLAUDE.md §7 rule 7).");

    /// <summary>A terminal reservation row was asked of a reservation that is no longer held.</summary>
    /// <param name="reservationId">The logical reservation.</param>
    /// <param name="state">The state its latest row is actually in.</param>
    public static InventoryRuleException ReservationNotHeld(Guid reservationId, ReservationState state)
        => new(
            "INVENTORY_RESERVATION_NOT_HELD",
            $"Reservation {reservationId} is {state}, not Held. A terminal row — consume, release or "
            + "expiry — closes a live hold; a closed chain is never written to again.");

    /// <summary>A hold was asked to lapse at a moment that is not a moment.</summary>
    public static InventoryRuleException ReservationExpiryInvalid()
        => new(
            "INVENTORY_RESERVATION_EXPIRY_INVALID",
            "A reservation expiry must be a real instant or omitted entirely.");

    /// <summary>A hold was asked for more than is available at its location.</summary>
    /// <param name="available">What is available to promise.</param>
    /// <param name="requested">What was asked to be held.</param>
    public static InventoryRuleException InsufficientAvailable(Quantity available, Quantity requested)
        => new(
            "INVENTORY_INSUFFICIENT_AVAILABLE",
            $"Only {available} is available to promise; {requested} was requested. Available never goes "
            + "negative: hold what exists and backorder the rest.");

    /// <summary>Available-to-promise figures were combined into a negative available.</summary>
    /// <param name="onHand">What is physically present.</param>
    /// <param name="reserved">What live holds speak for.</param>
    /// <param name="inStaging">What sits in staging bins.</param>
    public static InventoryRuleException AvailableWouldGoNegative(Quantity onHand, Quantity reserved, Quantity inStaging)
        => new(
            "INVENTORY_AVAILABLE_NEGATIVE",
            $"On hand {onHand} less reserved {reserved} less in staging {inStaging} is negative. "
            + "Available never goes negative in any company under any interleaving.");

    /// <summary>A terminal reservation row closed more than its chain holds.</summary>
    /// <param name="reserved">What the projection says is reserved.</param>
    /// <param name="closing">What the terminal row tried to close.</param>
    public static InventoryRuleException ReservationCloseExceedsHeld(Quantity reserved, Quantity closing)
        => new(
            "INVENTORY_RESERVATION_CLOSE_EXCEEDS_HELD",
            $"Closing {closing} against {reserved} reserved. A terminal row always carries its hold's "
            + "own quantity — anything else means the projection has drifted from the ledger.");

    /// <summary>A sourcing plan promises more of a line than was demanded, or less without a matching backorder.</summary>
    /// <param name="lineId">The demand line.</param>
    /// <param name="demanded">What the line asked for.</param>
    /// <param name="planned">What the plan covers plus backorders.</param>
    public static InventoryRuleException SourcingPlanUnbalanced(Guid lineId, Quantity demanded, Quantity planned)
        => new(
            "INVENTORY_SOURCING_PLAN_UNBALANCED",
            $"Line {lineId} demands {demanded} but the plan accounts for {planned}. "
            + "Allocated plus backordered must equal demanded on every line.");

    /// <summary>A split document set does not reconcile to its source.</summary>
    /// <param name="detail">The first mismatch, naming the line.</param>
    public static InventoryRuleException SplitReconciliationMismatch(string detail)
        => new(
            "INVENTORY_SPLIT_RECONCILIATION_MISMATCH",
            $"The split does not reconcile to its source: {detail} Every line lands on exactly one "
            + "document and the sums are exact — a mismatch here is a defect, not a rounding choice.");
}

/// <summary>An inventory action the caller is not permitted to take.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What the rule says.</param>
public sealed class InventoryForbiddenException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Forbidden)
{
    /// <summary>A caller without <c>registry.availability.view</c> asked for group availability.</summary>
    public static InventoryForbiddenException GroupAvailabilityNotPermitted()
        => new(
            "INVENTORY_GROUP_AVAILABILITY_NOT_PERMITTED",
            "Group availability spans companies. Reading it needs the registry.availability.view "
            + "permission; company-local availability needs only inventory.availability.view.");
}
