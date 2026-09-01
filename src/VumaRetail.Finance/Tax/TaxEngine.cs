using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Finance.Tax;

/// <summary>
/// Calculates tax from configured <see cref="TaxRule"/> data (CLAUDE.md §9 — a rules engine, never a
/// constant).
/// </summary>
/// <remarks>
/// Implements <see cref="ITaxCalculator"/>, the port Stage 09 added so a module outside Finance can
/// price a line without referencing the Finance assembly — the same shape
/// <see cref="IFinancialEventPoster"/> already has. This class stays the only implementation.
/// </remarks>
/// <param name="taxRules">Where tax rules are stored.</param>
public sealed class TaxEngine(ITaxRuleRepository taxRules) : ITaxCalculator
{
    /// <summary>
    /// Calculates net, tax and gross for a stated amount under the rule effective for a code on a date.
    /// </summary>
    /// <param name="taxCode">The tax code, for example <c>STANDARD</c>.</param>
    /// <param name="statedAmount">
    /// The amount as given. Whether this is treated as inclusive or exclusive of tax comes from the
    /// matched rule's own <see cref="TaxTreatment"/>, not from the caller.
    /// </param>
    /// <param name="asOf">The date the rule must be effective on — normally the document date.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="TaxRuleNotFoundException">No active rule matches the code on that date.</exception>
    public async Task<TaxCalculation> CalculateAsync(
        string taxCode, Money statedAmount, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taxCode);

        TaxRule rule = await taxRules.FindEffectiveAsync(taxCode, asOf, cancellationToken).ConfigureAwait(false)
            ?? throw new TaxRuleNotFoundException(taxCode, asOf);

        if (rule.Treatment is TaxTreatment.Inclusive)
        {
            // The stated amount already contains the tax: net = gross / (1 + rate).
            Money gross = statedAmount;
            Money net = (gross / (1m + rule.Rate)).RoundToCurrencyScale();
            Money tax = gross - net;
            return new TaxCalculation(rule.Code, net, tax, gross, rule.Rate);
        }
        else
        {
            Money net = statedAmount;
            Money tax = (net * rule.Rate).RoundToCurrencyScale();
            Money gross = net + tax;
            return new TaxCalculation(rule.Code, net, tax, gross, rule.Rate);
        }
    }
}
