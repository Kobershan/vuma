namespace VumaRetail.Domain.Planning;

/// <summary>The forecasting methods Stage 15 implements (ADR-149).</summary>
public enum ForecastMethod
{
    /// <summary>Mean of the last N weekly periods.</summary>
    MovingAverage = 0,

    /// <summary>Single exponential smoothing with tenant-configured alpha.</summary>
    ExponentialSmoothing = 1,

    /// <summary>The same week-position from the previous seasonal cycle.</summary>
    SeasonalNaive = 2,
}

/// <summary>ABC classes by demand share: A ≈ 80% of movement, B ≈ next 15%, C the tail.</summary>
public enum AbcClass
{
    /// <summary>Top movers — roughly 80% of demand value.</summary>
    A = 0,

    /// <summary>Middle movers — roughly the next 15%.</summary>
    B = 1,

    /// <summary>The long tail.</summary>
    C = 2,
}

/// <summary>XYZ classes by demand variability (coefficient of variation across weekly periods).</summary>
public enum XyzClass
{
    /// <summary>Steady demand (CV ≤ 0.5).</summary>
    X = 0,

    /// <summary>Fluctuating demand (0.5 &lt; CV ≤ 1.0).</summary>
    Y = 1,

    /// <summary>Erratic demand (CV &gt; 1.0).</summary>
    Z = 2,
}

/// <summary>Why a replenishment suggestion exists.</summary>
public enum SuggestionReason
{
    /// <summary>Available cover has fallen to or below the reorder point.</summary>
    BelowReorderPoint = 0,

    /// <summary>The forecast for the cover horizon exceeds what is on hand plus incoming.</summary>
    ForecastDrivenTopUp = 1,

    /// <summary>A sister company holds a fresh surplus the link permits moving.</summary>
    TransferSurplus = 2,
}

/// <summary>Where a suggestion wants its stock to come from.</summary>
public enum SuggestionSource
{
    /// <summary>Buy it: accept to raise a supplier-free purchase requisition (Stage 12).</summary>
    Procurement = 0,

    /// <summary>Move it: accept to transfer from the named source location.</summary>
    Transfer = 1,
}

/// <summary>Where a replenishment suggestion stands.</summary>
public enum SuggestionStatus
{
    /// <summary>Raised by the scheduled run, awaiting a decision.</summary>
    Open = 0,

    /// <summary>Accepted — exactly one downstream document exists.</summary>
    Accepted = 1,

    /// <summary>Rejected — creates nothing, kept for the audit.</summary>
    Rejected = 2,

    /// <summary>Lapsed unanswered past its expiry — creates nothing.</summary>
    Expired = 3,
}

/// <summary>Where a markdown plan stands.</summary>
public enum MarkdownPlanStatus
{
    /// <summary>Being written. Creates nothing in sales.</summary>
    Draft = 0,

    /// <summary>With Stage 05 — waiting on an approval request.</summary>
    PendingApproval = 1,

    /// <summary>Approved, but no step activated yet — still no pricing effect.</summary>
    Approved = 2,

    /// <summary>At least one step activated — a live promotion exists in Stage 10.</summary>
    Active = 3,

    /// <summary>Superseded by a newer version. History is retained.</summary>
    Amended = 4,

    /// <summary>Withdrawn. Any live promotion was deactivated through Stage 10.</summary>
    Cancelled = 5,

    /// <summary>Ran its course. Terminal.</summary>
    Completed = 6,
}

