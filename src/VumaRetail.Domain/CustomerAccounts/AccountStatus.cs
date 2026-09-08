namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>Where a customer credit account stands. Hold and closure are explicit, never silent.</summary>
public enum AccountStatus
{
    /// <summary>Trading normally. Tender-time checks pass while exposure fits the limit.</summary>
    Active = 0,

    /// <summary>Frozen by credit control. Tender refuses until <c>ReleaseAccountHoldCommand</c>.</summary>
    OnHold = 1,

    /// <summary>Permanently closed. No new charges; existing balances still collectible.</summary>
    Closed = 2
}
