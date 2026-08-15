using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Primitives;
using VumaRetail.Finance.Tax;

namespace VumaRetail.Finance.Queries;

/// <summary>
/// Previews what a tax code and amount would calculate to, without creating any document.
/// </summary>
/// <param name="TaxCode">The tax code.</param>
/// <param name="Amount">The amount as entered.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="AsOf">The date to evaluate the rule as of.</param>
public sealed record CalculateTaxQuery(string TaxCode, decimal Amount, string Currency, DateOnly AsOf)
    : IQuery<TaxCalculation>;

/// <summary>Handles <see cref="CalculateTaxQuery"/>.</summary>
/// <param name="taxEngine">Calculates tax from configured rules.</param>
public sealed class CalculateTaxQueryHandler(TaxEngine taxEngine) : IQueryHandler<CalculateTaxQuery, TaxCalculation>
{
    /// <inheritdoc />
    /// <exception cref="Domain.Finance.TaxRuleNotFoundException">No rule matches the code on that date.</exception>
    public Task<TaxCalculation> HandleAsync(CalculateTaxQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return taxEngine.CalculateAsync(query.TaxCode, new Money(query.Amount, query.Currency), query.AsOf, cancellationToken);
    }
}
