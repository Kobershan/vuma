using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Warehouse;

/// <summary>Something warehouse-owned was asked for that does not exist, or is not this tenant's.</summary>
/// <param name="what">What was being looked for, for example <c>bin</c>.</param>
/// <param name="id">The identifier that found nothing.</param>
public sealed class WarehouseNotFoundException(string what, Guid id)
    : DomainException("WAREHOUSE_NOT_FOUND", $"No {what} with id {id}.", DomainProblemKind.NotFound);

/// <summary>Something warehouse-owned must be unique and is not.</summary>
/// <param name="code">The stable machine-readable code for the specific collision.</param>
/// <param name="message">What collided, in words.</param>
public sealed class WarehouseConflictException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Conflict)
{
    /// <summary>A bin code already used at this location.</summary>
    public static WarehouseConflictException BinCode(string code)
        => new("WAREHOUSE_BIN_CODE_TAKEN", $"Bin code '{code}' is already used at this location.");

    /// <summary>A zone code already used at this location.</summary>
    public static WarehouseConflictException ZoneCode(string code)
        => new("WAREHOUSE_ZONE_CODE_TAKEN", $"Zone code '{code}' is already used at this location.");
}

/// <summary>A warehouse business rule was broken.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What the rule says.</param>
public sealed class WarehouseRuleException(string code, string message) : DomainException(code, message)
{
    /// <summary>A bin/zone movement named neither an item nor a variant, or named both.</summary>
    public static WarehouseRuleException ExactlyOneItemOrVariantRequired()
        => new(
            "WAREHOUSE_EXACTLY_ONE_ITEM_OR_VARIANT",
            "A bin-level movement identifies exactly one of an item (when it has no variants) or a "
            + "variant — never both, never neither.");

    /// <summary>A quantity was zero or negative where a positive amount is required.</summary>
    public static WarehouseRuleException QuantityMustBePositive()
        => new("WAREHOUSE_QUANTITY_MUST_BE_POSITIVE", "The quantity must be greater than zero.");

    /// <summary>A bin was asked to relieve more than it holds.</summary>
    /// <param name="available">What the bin holds.</param>
    /// <param name="requested">What was asked to be relieved.</param>
    public static WarehouseRuleException InsufficientBinStock(Quantity available, Quantity requested)
        => new(
            "WAREHOUSE_INSUFFICIENT_BIN_STOCK",
            $"Only {available} is held in this bin; {requested} was requested.");

    /// <summary>A movement's unit of measure does not match the bin balance it is posting against.</summary>
    public static WarehouseRuleException UnitOfMeasureMismatch(string expected, string actual)
        => new(
            "WAREHOUSE_UOM_MISMATCH",
            $"This bin balance is held in '{expected}'; '{actual}' cannot be posted against it directly.");

    /// <summary>A putaway task was confirmed for more than its remaining unbinned quantity.</summary>
    public static WarehouseRuleException PutawayExceedsRemaining(Quantity remaining, Quantity requested)
        => new(
            "WAREHOUSE_PUTAWAY_EXCEEDS_REMAINING",
            $"Only {remaining} of this putaway task remains unbinned; {requested} was confirmed.");

    /// <summary>A putaway task was confirmed or cancelled after it was already fully confirmed or cancelled.</summary>
    public static WarehouseRuleException PutawayNotPending()
        => new(
            "WAREHOUSE_PUTAWAY_NOT_PENDING",
            "This putaway task is already fully confirmed or cancelled.");

    /// <summary>A pick task was allocated, confirmed or cancelled from the wrong state.</summary>
    public static WarehouseRuleException PickTaskWrongStatus(PickTaskStatus expected, PickTaskStatus actual)
        => new(
            "WAREHOUSE_PICK_TASK_WRONG_STATUS",
            $"This pick task must be {expected} for this operation; it is {actual}.");

    /// <summary>A pick was confirmed for more than its allocated quantity.</summary>
    public static WarehouseRuleException PickExceedsAllocated(Quantity allocated, Quantity requested)
        => new(
            "WAREHOUSE_PICK_EXCEEDS_ALLOCATED",
            $"Only {allocated} was allocated to this pick task; {requested} was confirmed.");

    /// <summary>A pick wave transitioned from the wrong state.</summary>
    public static WarehouseRuleException PickWaveWrongStatus(PickWaveStatus expected, PickWaveStatus actual)
        => new(
            "WAREHOUSE_PICK_WAVE_WRONG_STATUS",
            $"This pick wave must be {expected} for this operation; it is {actual}.");

    /// <summary>A wave was shipped or packed with nothing picked.</summary>
    public static WarehouseRuleException NothingPicked()
        => new("WAREHOUSE_NOTHING_PICKED", "This wave has no picked quantity to pack or ship.");

    /// <summary>A cycle count was asked to accept a count or be finalized after it was already finalized.</summary>
    public static WarehouseRuleException CycleCountAlreadyFinalized()
        => new(
            "WAREHOUSE_CYCLE_COUNT_ALREADY_FINALIZED",
            "This cycle count is already finalized. A correction is a new count, not an edit to a "
            + "finalized one (CLAUDE.md §7 rule 7).");

    /// <summary>No candidate bin held enough stock to satisfy a pick allocation.</summary>
    public static WarehouseRuleException InsufficientStockToAllocate(Quantity requested, Quantity available)
        => new(
            "WAREHOUSE_INSUFFICIENT_STOCK_TO_ALLOCATE",
            $"{requested} was requested but only {available} is held across candidate bins for this "
            + "stock-keeping unit at this location.");
}
