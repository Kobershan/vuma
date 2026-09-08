namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>What a stokvel payout turns a member's balance into.</summary>
public enum StokvelPayoutKind
{
    /// <summary>Goods off the shelf, settled as a normal sale.</summary>
    Goods = 0,

    /// <summary>A predefined hamper basket, settled as a normal sale.</summary>
    Hamper = 1,

    /// <summary>Cash out.</summary>
    Cash = 2,

    /// <summary>Store credit.</summary>
    StoreCredit = 3,
}
