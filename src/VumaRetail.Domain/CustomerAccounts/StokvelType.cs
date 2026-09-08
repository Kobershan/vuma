using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>What a stokvel saves toward.</summary>
public enum StokvelType
{
    /// <summary>Cash savings, paid out as cash or store credit.</summary>
    Savings = 0,

    /// <summary>Grocery or hamper savings, paid out as goods.</summary>
    GroceryHamper = 1,

    /// <summary>Burial savings.</summary>
    Burial = 2,

    /// <summary>Pooled buying for investment.</summary>
    InvestmentBuying = 3,
}
