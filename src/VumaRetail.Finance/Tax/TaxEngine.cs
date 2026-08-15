using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Finance.Tax;

/// <summary>The result of applying a <see cref="TaxRule"/> to an amount.</summary>
/// <param name="TaxCode">The rule's code.</param>
/// <param name="NetAmount">The amount excluding tax.</param>
/// <param name="TaxAmount">The tax.</param>
/// <param name="GrossAmount">Net plus tax.</param>
/// <param name="Rate">The rate that was applied, as a fraction.</param>
public sealed record TaxCalculation(string TaxCode, Money NetAmount, Money TaxAmount, Money GrossAmount, decimal Rate);

/// <summary>
/// Calculates tax from configured <see cref="TaxRule"/> data (CLAUDE.md §9 — a rules engine, never a
/// constant).
/// </summary>
/// <param name="taxRules">Where tax rules are stored.</param>
public sealed class TaxEngine(ITaxRuleRepository taxRules)
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
