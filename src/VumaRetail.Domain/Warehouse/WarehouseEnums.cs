namespace VumaRetail.Domain.Warehouse;

/// <summary>What kind of work a <see cref="Zone"/> is used for.</summary>
public enum ZoneType
{
    /// <summary>Where inbound deliveries land before putaway.</summary>
    Receiving = 0,

    /// <summary>General shelving/pallet storage.</summary>
    Storage = 1,

    /// <summary>Fast-moving stock staged for quick picking.</summary>
    Picking = 2,

    /// <summary>Where picked lines are consolidated and packed.</summary>
    Packing = 3,

    /// <summary>The outbound dock a shipment leaves from.</summary>
    Shipping = 4,

    /// <summary>Where returned or rejected stock is held pending disposition.</summary>
    Returns = 5,
}

/// <summary>What kind of storage unit a <see cref="Bin"/> is.</summary>
public enum BinType
{
    /// <summary>A fixed shelf location, typically small-quantity, hand-pick.</summary>
    Shelf = 0,

    /// <summary>A full-pallet storage position.</summary>
    Pallet = 1,

    /// <summary>An unslotted bulk-storage area.</summary>
    Bulk = 2,

    /// <summary>A temporary staging position — post-pick, pre-pack, or similar.</summary>
    Staging = 3,

    /// <summary>A dock door or receiving/shipping apron position.</summary>
    Dock = 4,
}

/// <summary>What kind of event a <see cref="BinStockMovement"/> records.</summary>
public enum BinStockMovementType
{
    /// <summary>Stock shelved into a bin by a putaway.</summary>
    PutawayIn = 0,

    /// <summary>Stock leaving unbinned/dock storage as the source side of a putaway.</summary>
    PutawayOut = 1,

    /// <summary>Stock reserved out of a bin for a pick.</summary>
    PickReserve = 2,

    /// <summary>A previously reserved pick released back to the bin (a cancelled or corrected allocation).</summary>
    PickRelease = 3,

    /// <summary>Stock arriving at a bin as the destination side of an internal bin-to-bin move.</summary>
    InternalTransferIn = 4,

    /// <summary>Stock leaving a bin as the source side of an internal bin-to-bin move.</summary>
    InternalTransferOut = 5,
}

/// <summary>What kind of document a <see cref="BinStockMovement"/> correlates to.</summary>
public enum BinStockReferenceType
{
    /// <summary>Correlates to a <see cref="PutawayTask"/>.</summary>
    Putaway = 0,

    /// <summary>Correlates to a <see cref="PickTask"/>.</summary>
    Pick = 1,

    /// <summary>Correlates to an internal bin-to-bin transfer's shared id.</summary>
    InternalTransfer = 2,
}

/// <summary>What kind of document put a <see cref="PutawayTask"/>'s stock on the dock, unbinned.</summary>
public enum PutawaySourceReferenceType
{
    /// <summary>A Stage 12 <c>procurement.goods_receipt_lines</c> row.</summary>
    GoodsReceipt = 0,

    /// <summary>A Stage 08 manual receipt (<c>inventory.stock_ledger_entries</c>).</summary>
    ManualReceipt = 1,

    /// <summary>A Stage 08 positive adjustment.</summary>
    Adjustment = 2,
}

/// <summary>Where a <see cref="PutawayTask"/> stands.</summary>
public enum PutawayStatus
{
    /// <summary>Some or all of the task's quantity is still unbinned.</summary>
    Pending = 0,

    /// <summary>The task's full quantity has been shelved.</summary>
    Confirmed = 1,

    /// <summary>The task was abandoned before it was fully confirmed.</summary>
    Cancelled = 2,
}

/// <summary>Where a <see cref="PickWave"/> stands.</summary>
public enum PickWaveStatus
{
    /// <summary>Lines may still be added; nothing has been allocated.</summary>
    Open = 0,

    /// <summary>Lines are allocated to bins; picking may begin.</summary>
    Released = 1,

    /// <summary>Every line has been picked or short-picked.</summary>
    Picked = 2,

    /// <summary>The wave has been packed.</summary>
    Packed = 3,

    /// <summary>The wave has shipped — stock has left the location.</summary>
    Shipped = 4,

    /// <summary>The wave was abandoned before shipping.</summary>
    Cancelled = 5,
}

/// <summary>Where a <see cref="PickTask"/> stands.</summary>
public enum PickTaskStatus
{
    /// <summary>Not yet allocated to a bin.</summary>
    Pending = 0,

    /// <summary>Allocated to a bin; awaiting confirmation.</summary>
    Allocated = 1,

    /// <summary>Confirmed picked in full.</summary>
    Picked = 2,

    /// <summary>Confirmed picked for less than the allocated quantity.</summary>
    ShortPicked = 3,

    /// <summary>Abandoned before being picked.</summary>
    Cancelled = 4,
}

/// <summary>Where a <see cref="CycleCount"/> stands.</summary>
public enum CycleCountStatus
{
    /// <summary>Counts may still be recorded.</summary>
    Open = 0,

    /// <summary>Every line's variance has been posted. No further counts may be recorded.</summary>
    Finalized = 1,
}
