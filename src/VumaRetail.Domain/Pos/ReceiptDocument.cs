using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Pos;

/// <summary>
/// The receipt a completed sale produces — everything that goes on the slip, and nothing about how it
/// is printed.
/// </summary>
/// <remarks>
/// <para>
/// A value object rather than a table. The receipt is a projection of the sale, so storing it would
/// create a second copy of the same facts that could drift from the first; what <em>is</em> stored is
/// <see cref="ReceiptPrint"/>, the record that a copy was produced and by whom.
/// </para>
/// <para>
/// It lives in the domain rather than in <c>VumaRetail.Contracts</c> because two very different things
/// consume it: the API, which serialises it, and <c>VumaRetail.Hardware</c>, which renders it to
/// ESC/POS bytes for an 80mm thermal printer. Putting it here means the renderer and the API agree on
/// what a receipt is by construction instead of by a mapping somebody maintains.
/// </para>
/// <para>
/// The tax summary is a legal requirement, not a nicety: a South African VAT invoice must show the tax
/// broken out per rate, which is why <see cref="TaxLines"/> is grouped by code rather than being a
/// single total.
/// </para>
/// </remarks>
/// <param name="SaleNumber">The receipt number.</param>
/// <param name="StoreName">The trading name at the top of the slip.</param>
/// <param name="StoreAddress">The store's address, already formatted for printing, or <c>null</c>.</param>
/// <param name="TaxNumber">The tenant's VAT registration number, or <c>null</c> if not registered.</param>
/// <param name="CompletedAt">When the sale completed, UTC. Rendered in the store's timezone at the edge.</param>
/// <param name="TerminalId">Which till.</param>
/// <param name="OperatorName">Who served the customer, for the "served by" line.</param>
/// <param name="Lines">What was bought.</param>
/// <param name="TaxLines">Tax broken out per rate.</param>
/// <param name="Net">The sale excluding tax.</param>
/// <param name="Tax">The tax.</param>
/// <param name="Gross">What was owed.</param>
/// <param name="Tenders">How it was paid.</param>
/// <param name="ChangeGiven">What was handed back.</param>
/// <param name="IsReprint">True when this is not the first printing. Printed on the slip, prominently.</param>
/// <param name="Footer">The tenant's closing message, or <c>null</c>.</param>
public sealed record ReceiptDocument(
    string SaleNumber,
    string StoreName,
    string? StoreAddress,
    string? TaxNumber,
    DateTimeOffset CompletedAt,
    Guid TerminalId,
    string? OperatorName,
    IReadOnlyList<ReceiptLine> Lines,
    IReadOnlyList<ReceiptTaxLine> TaxLines,
    Money Net,
    Money Tax,
    Money Gross,
    IReadOnlyList<ReceiptTender> Tenders,
    Money ChangeGiven,
    bool IsReprint,
    string? Footer);

/// <summary>One line on the slip.</summary>
/// <param name="Description">What it was, as snapshotted at ring-up.</param>
/// <param name="Quantity">How much.</param>
/// <param name="UnitPrice">What one cost.</param>
/// <param name="DiscountAmount">The discount taken off, if any. Printed as its own line when non-zero.</param>
/// <param name="Gross">What the line came to.</param>
/// <param name="TaxCode">The tax code, printed as the per-line marker the tax summary keys on.</param>
public sealed record ReceiptLine(
    string Description,
    Quantity Quantity,
    Money UnitPrice,
    Money DiscountAmount,
    Money Gross,
    string TaxCode);

/// <summary>One rate's worth of tax, for the summary block a VAT invoice must carry.</summary>
/// <param name="TaxCode">The code, for example <c>STANDARD</c>.</param>
/// <param name="Net">The net at this rate.</param>
/// <param name="Tax">The tax at this rate.</param>
public sealed record ReceiptTaxLine(string TaxCode, Money Net, Money Tax);

/// <summary>One payment, as it appears at the foot of the slip.</summary>
/// <param name="Type">How it was paid.</param>
/// <param name="Amount">How much.</param>
/// <param name="Reference">
/// The card authorisation or voucher serial, masked or truncated by the caller if it needs to be. This
/// type does no masking of its own — it does not know what it is holding.
/// </param>
public sealed record ReceiptTender(TenderType Type, Money Amount, string? Reference);
