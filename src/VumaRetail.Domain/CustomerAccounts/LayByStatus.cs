namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>Where a lay-by agreement stands. Signed money moves it forward; only completion and
/// cancellation move money sideways.</summary>
public enum LayByStatus
{
    /// <summary>Opened and printed, not yet signed. No money moves until activation.</summary>
    Draft = 0,

    /// <summary>Signed and paying. Instalments accepted; stock held.</summary>
    Active = 1,

    /// <summary>Fully paid and converted to exactly one sale. Terminal.</summary>
    Completed = 2,

    /// <summary>Cancelled per the snapshotted terms: refund less fee. Terminal.</summary>
    Cancelled = 3,

    /// <summary>The expiry date passed unpaid. Terminal; stock released.</summary>
    Expired = 4
}
