namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>Where a stokvel group stands in its cycle.</summary>
public enum StokvelStatus
{
    /// <summary>Forming: members may join, contributions not yet taken.</summary>
    Forming = 0,

    /// <summary>Active: contributions taken, benefits accrue.</summary>
    Active = 1,

    /// <summary>PayingOut: no new members, payouts settle.</summary>
    PayingOut = 2,

    /// <summary>Closed: cycle finished, rows retained for audit.</summary>
    Closed = 3,
}
