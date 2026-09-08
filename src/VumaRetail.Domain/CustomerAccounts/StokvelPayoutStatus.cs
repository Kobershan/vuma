namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>Where a stokvel payout stands.</summary>
public enum StokvelPayoutStatus
{
    /// <summary>Requested: availability checked, approval pending.</summary>
    Requested = 0,

    /// <summary>Approved: may settle.</summary>
    Approved = 1,

    /// <summary>Settled: money moved, sale stamped where one exists.</summary>
    Settled = 2,
}
