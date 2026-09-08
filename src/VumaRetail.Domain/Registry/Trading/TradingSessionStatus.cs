namespace VumaRetail.Domain.Registry.Trading;

/// <summary>Status of a mixed-basket trading session (Stage 09b, ADR-125).</summary>
public enum TradingSessionStatus
{
    /// <summary>Lines may be added and voided; no tender captured.</summary>
    Open = 0,

    /// <summary>One tender captured and allocated; lines frozen; may complete or void.</summary>
    Tendered = 1,

    /// <summary>A completion saga is running legs. Transient — never a resting state.</summary>
    Completing = 2,

    /// <summary>Every segment posted its sale, invoice and receipt. Terminal.</summary>
    Completed = 3,

    /// <summary>Abandoned with a reason. Reservations released, nothing posted. Terminal.</summary>
    Voided = 4,

    /// <summary>
    /// A completion attempt failed and compensation ran. The customer-facing state is
    /// tendered again (tender still held, lines intact) with <see
    /// cref="TradingSession.FailureReason"/> naming the cause; posted-but-unwound
    /// invoices are listed for back-office credit-noting (ADR-145).
    /// </summary>
    CompletionFailed = 5,
}

/// <summary>Status of one company's segment within a trading session.</summary>
public enum TradingSegmentStatus
{
    /// <summary>Lines still being added.</summary>
    Building = 0,

    /// <summary>Tender captured; the segment's allocation is fixed.</summary>
    Tendered = 1,

    /// <summary>The leg posted its sale, invoice and receipt.</summary>
    Posted = 2,

    /// <summary>The leg was compensated (sale voided, receipt reversed, holds released).</summary>
    Compensated = 3,
}
