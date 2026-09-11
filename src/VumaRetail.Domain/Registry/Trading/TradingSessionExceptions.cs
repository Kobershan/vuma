namespace VumaRetail.Domain.Registry.Trading;

/// <summary>Coded refusals for the mixed-basket trading session (Stage 09b).</summary>
public sealed class TradingSessionException(string code, string message) : InvalidOperationException(message)
{
    /// <summary>Machine-readable refusal code, for ProblemDetails and till messaging.</summary>
    public string Code { get; } = code;

    /// <summary>A sister company's line was scanned with no active <c>SharedTill</c> link.</summary>
    public static TradingSessionException LinkRequired(Guid sessionCompanyId, Guid lineCompanyId)
        => new("TRADING_LINK_REQUIRED",
            $"Company {lineCompanyId} has no active SharedTill link with company {sessionCompanyId}. " +
            "The line was refused at scan time; the basket keeps only linked companies' lines.");

    /// <summary>The tender does not cover the basket gross.</summary>
    public static TradingSessionException TenderNotCovered(decimal tendered, decimal gross, string currency)
        => new("TRADING_TENDER_NOT_COVERED",
            $"Tendered {tendered:F2} {currency} does not cover the basket gross of {gross:F2} {currency}.");

    /// <summary>A cashier allocation override does not sum to the tender exactly.</summary>
    public static TradingSessionException AllocationMismatch(decimal allocated, decimal tendered, string currency)
        => new("TRADING_ALLOCATION_MISMATCH",
            $"Allocations total {allocated:F2} {currency} but the tender is {tendered:F2} {currency}. " +
            "Allocate the full tender, to the cent.");

    /// <summary>The session is in a status that forbids the attempted mutation.</summary>
    public static TradingSessionException IllegalTransition(TradingSessionStatus status, string attempted)
        => new("TRADING_ILLEGAL_TRANSITION",
            $"A {status} trading session cannot {attempted}.");

    /// <summary>No routing candidate exists for a barcode (unknown or retired).</summary>
    public static TradingSessionException UnroutableBarcode(string barcode)
        => new("TRADING_UNROUTABLE_BARCODE",
            $"Barcode '{barcode}' resolves to no company. It cannot be added to a mixed basket.");

    /// <summary>A barcode is published by more than one company and needs an explicit selection.</summary>
    public static TradingSessionException AmbiguousBarcode(string barcode)
        => new("TRADING_AMBIGUOUS_BARCODE",
            $"Barcode '{barcode}' resolves to more than one company. Select the owning company; the till will not guess.");

    /// <summary>A line's currency differs from the session's (§4.13's lesson).</summary>
    public static TradingSessionException CurrencyMismatch(string sessionCurrency, string lineCurrency)
        => new("TRADING_CURRENCY_MISMATCH",
            $"Line currency {lineCurrency} does not match session currency {sessionCurrency}.");

    /// <summary>A return names an invoice from another company's segment.</summary>
    public static TradingSessionException ReturnWrongCompany(string invoiceA, string invoiceB)
        => new("TRADING_RETURN_WRONG_COMPANY",
            $"That line belongs to invoice {invoiceA}, not {invoiceB}. " +
            "A mixed-basket return credits the origin invoice's company — there is no cross-company credit note.");
}
