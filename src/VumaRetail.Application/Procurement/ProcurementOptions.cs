using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Procurement;

/// <summary>
/// The two tolerances buying runs inside: how much more than was ordered a delivery may contain, and
/// how far a supplier's price may drift from the order before nobody pays it.
/// </summary>
/// <remarks>
/// <para>
/// Configuration rather than constants, for the reason <c>CLAUDE.md</c> §9 gives and
/// <c>ImportOptions</c> restates: these are commercial policy, they differ by trade, and a fresh-produce
/// wholesaler who accepts 10% over-delivery as normal and a pharmacy who accepts none are both right.
/// </para>
/// <para>
/// The defaults are the conservative end. <see cref="OverReceiptTolerancePercentage"/> is <b>zero</b>:
/// a store that has not thought about this should not silently accept and pay for stock it did not
/// order, and the refusal message says exactly what to do about it. The price tolerances are the
/// smallest numbers that stop a match blocking over arithmetic — 2% of the line, or R10, whichever is
/// larger.
/// </para>
/// <para>
/// <b>Both are snapshotted onto the documents they are applied to.</b> Changing them here does not
/// restate a match that has already been run — see <c>SupplierInvoiceMatch</c>'s remarks on why an audit
/// record that re-judges itself under this month's policy is not an audit record.
/// </para>
/// </remarks>
public sealed class ProcurementOptions
{
    /// <summary>The configuration section these are bound from.</summary>
    public const string SectionName = "Vuma:Procurement";

    /// <summary>
    /// How far past the ordered quantity a cumulative delivery may go before the line is refused, as a
    /// percentage (business rule 6). Zero — the default — means no over-receipt at all.
    /// </summary>
    public decimal OverReceiptTolerancePercentage { get; set; }

    /// <summary>
    /// How far a supplier's extended line price may differ from the order's before the line blocks, as
    /// a percentage of the line (business rule 12).
    /// </summary>
    public decimal PriceTolerancePercentage { get; set; } = 2m;

    /// <summary>
    /// The absolute per-line floor, in <see cref="ToleranceCurrency"/>. A variance inside <em>either</em>
    /// this or <see cref="PriceTolerancePercentage"/> passes.
    /// </summary>
    /// <remarks>
    /// The floor is why a 2% rule is usable. Without it, 2% of a R3.00 line is six cents, and a shop
    /// would be holding up a supplier's payment over six cents — which nobody does, so in practice
    /// somebody would raise the percentage until it stopped catching anything worth catching.
    /// </remarks>
    public decimal PriceToleranceFloorAmount { get; set; } = 10m;

    /// <summary>
    /// The currency <see cref="PriceToleranceFloorAmount"/> is stated in. Defaults to <c>ZAR</c>,
    /// <c>CLAUDE.md</c> §9's localisation default.
    /// </summary>
    /// <remarks>
    /// An absolute tolerance has to be denominated in something, and a floor stated in rands means
    /// nothing on a dollar-denominated order. Where the configured currency does not match the order's,
    /// the floor is dropped and the percentage alone applies — see
    /// <c>ThreeWayMatchEngine.ResolveToleranceFloor</c>. Converting it would need a rate and a rate
    /// date, which is Finance's job and not a tolerance's.
    /// </remarks>
    public string ToleranceCurrency { get; set; } = "ZAR";

    /// <summary>The floor as money, or <c>null</c> when it is not denominated in the given currency.</summary>
    /// <param name="currency">The document's currency.</param>
    public Money? FloorFor(string currency)
        => string.Equals(ToleranceCurrency, currency, StringComparison.OrdinalIgnoreCase)
            ? new Money(PriceToleranceFloorAmount, currency)
            : null;
}
