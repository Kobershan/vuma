namespace VumaRetail.Domain.FieldSales;

/// <summary>Coded refusals for field sales (Stage 14b).</summary>
public sealed class FieldSalesException(string code, string message) : InvalidOperationException(message)
{
    /// <summary>Machine-readable refusal code, for ProblemDetails and rep messaging.</summary>
    public string Code { get; } = code;

    /// <summary>The rep may not sell for, read, or quote this scope.</summary>
    public static FieldSalesException Forbidden(string what)
        => new("REP_FORBIDDEN",
            $"This rep is not permitted to {what}. The attempt was refused, not filtered.");

    /// <summary>The pro forma is expired; approval needs a re-price first.</summary>
    public static FieldSalesException Expired(string number)
        => new("PROFORMA_EXPIRED",
            $"Pro forma {number} is past its expiry. Re-price it before approval.");

    /// <summary>The status forbids the attempted mutation.</summary>
    public static FieldSalesException IllegalTransition(ProFormaStatus status, string attempted)
        => new("PROFORMA_ILLEGAL_TRANSITION",
            $"A {status} pro forma cannot {attempted}.");

    /// <summary>Approval refused: group credit exhausted, with the position stated.</summary>
    public static FieldSalesException CreditExhausted(decimal limit, decimal available, string currency)
        => new("PROFORMA_CREDIT_EXHAUSTED",
            $"Group credit is exhausted: limit {limit:F2} {currency}, available {available:F2} {currency}. " +
            "Nothing was reserved.");
}
