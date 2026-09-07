namespace VumaRetail.Domain.Sales.Quotes;

/// <summary>The lifecycle of a quote: a non-binding price promise, never a promise of stock.</summary>
public enum QuoteStatus
{
    /// <summary>Being built. Lines may be added; nothing is promised yet.</summary>
    Draft = 0,

    /// <summary>Prices locked and handed to the customer. The validity clock is running.</summary>
    Issued = 1,

    /// <summary>The customer said yes inside the validity window. Ready to convert.</summary>
    Accepted = 2,

    /// <summary>The customer said no. Terminal.</summary>
    Rejected = 3,

    /// <summary>The validity window lapsed or the shop withdrew it. Terminal.</summary>
    Expired = 4,

    /// <summary>Converted into an order or a sale. Terminal; the quote itself never trades.</summary>
    Converted = 5
}
