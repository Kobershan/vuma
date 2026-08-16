using System.Globalization;
using System.Text;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;

namespace VumaRetail.Application.Sales.Pricing;

/// <inheritdoc cref="IPriceResolver" />
/// <param name="priceLists">The candidate lists for a stock-keeping unit at a store on a day.</param>
/// <param name="promotions">The day's live specials for the store.</param>
/// <remarks>
/// <para>
/// Two reads and a pure function. Everything that decides an amount lives in
/// <see cref="PromotionEngine"/>, which has no dependencies at all; this class only chooses the list,
/// enforces the currency rule and writes the explanation. The split is what makes the arithmetic
/// table-testable and this class trivially fakeable.
/// </para>
/// <para>
/// <b>Business rule 2 is honoured by what is returned, not by what is computed.</b> The unit price
/// leaves here at <see cref="Money"/>'s full four-place scale and the discount is a whole-line amount,
/// so the one rounding to the currency's scale happens on the extended figure — once, in
/// <see cref="PriceResolution.NetPayable"/> for display, and once again in POS when it stores the line.
/// A resolver that returned a two-place unit price would have made §4.14's cent unavoidable for every
/// caller.
/// </para>
/// </remarks>
public sealed class PriceResolver(IPriceListRepository priceLists, IPromotionRepository promotions)
    : IPriceResolver
{
    /// <inheritdoc />
    public async Task<PriceResolution> ResolveAsync(
        PriceResolutionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ItemId.HasValue == request.ItemVariantId.HasValue)
        {
            throw SalesRuleException.ExactlyOneItemOrVariantRequired();
        }

        if (request.Quantity <= 0m)
        {
            throw SalesRuleException.QuantityMustBePositive();
        }

        IReadOnlyList<PriceList> candidates = await priceLists
            .ListCandidatesAsync(
                request.ItemId, request.ItemVariantId, request.StoreId, request.OnDate, cancellationToken)
            .ConfigureAwait(false);

        (PriceList List, PriceListLine Line)? winner = ChooseList(candidates, request);

        if (winner is not { } priced)
        {
            throw new SalesNotFoundException(
                "price for that item on any effective price list",
                request.ItemId ?? request.ItemVariantId!.Value);
        }

        // Business rule 10. §4.13 is the record of a currency mismatch reaching a till as an unhandled
        // exception: the terminal bricked and stayed bricked. This is the same class of mistake caught
        // one layer earlier and answered with a code the caller can act on.
        if (!string.Equals(priced.List.Currency, request.Currency, StringComparison.Ordinal))
        {
            throw SalesRuleException.CurrencyMismatch(request.Currency, priced.List.Currency);
        }

        Money unitPrice = priced.Line.UnitPrice;

        IReadOnlyList<Promotion> live = await promotions
            .ListLiveAsync(request.StoreId, request.OnDate, cancellationToken)
            .ConfigureAwait(false);

        PromotionOutcome outcome = PromotionEngine.Apply(
            new PromotionContext(
                request.ItemId,
                request.ItemVariantId,
                request.CategoryCode,
                request.Quantity,
                unitPrice,
                request.OnDate,
                request.AtTime),
            live);

        Money extended = unitPrice * request.Quantity;
        Money netPayable = (extended - outcome.DiscountAmount).RoundToCurrencyScale();

        return new PriceResolution(
            priced.List.Id,
            priced.List.Code,
            priced.Line.Id,
            priced.List.PricesIncludeTax,
            unitPrice,
            extended,
            outcome.DiscountAmount,
            netPayable,
            outcome.Applied,
            Explain(request, priced.List, priced.Line, extended, outcome, netPayable));
    }

    /// <summary>
    /// Picks the list that prices this line: a store-scoped list before a tenant-wide one, then the
    /// highest priority, then the lowest code.
    /// </summary>
    /// <remarks>
    /// Scope outranks priority deliberately. A branch running its own prices has set them for a reason
    /// local to that branch, and head office raising the priority of a national list should not silently
    /// override it — that is an argument the two of them should have in the configuration, not one the
    /// resolver settles by arithmetic. Code is the final tiebreak so the answer is total rather than
    /// dependent on row order, which is the same guarantee <see cref="PromotionEngine"/> gives.
    /// </remarks>
    private static (PriceList List, PriceListLine Line)? ChooseList(
        IReadOnlyList<PriceList> candidates, PriceResolutionRequest request)
    {
        IEnumerable<PriceList> ranked = candidates
            .Where(list => list.IsEffectiveOn(request.OnDate))
            .Where(list => list.StoreId is null || list.StoreId == request.StoreId)
            .OrderByDescending(list => list.StoreId is not null)
            .ThenByDescending(list => list.Priority)
            .ThenBy(list => list.Code, StringComparer.Ordinal);

        foreach (PriceList list in ranked)
        {
            // The first list that actually prices the item wins. A higher-priority list that happens
            // not to carry this line does not block a lower one — that would make adding a promotional
            // list silently un-price everything it omitted.
            if (list.FindPrice(request.ItemId, request.ItemVariantId, request.Quantity) is { } line)
            {
                return (list, line);
            }
        }

        return null;
    }

    private static string Explain(
        PriceResolutionRequest request,
        PriceList list,
        PriceListLine line,
        Money extended,
        PromotionOutcome outcome,
        Money netPayable)
    {
        StringBuilder explanation = new();

        explanation.Append(CultureInfo.InvariantCulture, $"Price list {list.Code} ({list.Kind}) prices this at ")
            .Append(Format(line.UnitPrice))
            .Append(CultureInfo.InvariantCulture, $" each from a quantity of {Trim(line.MinimumQuantity)}");

        explanation.Append(CultureInfo.InvariantCulture, $"; {Trim(request.Quantity)} × ")
            .Append(Format(line.UnitPrice))
            .Append(" = ")
            .Append(Format(extended))
            .Append('.');

        foreach (AppliedPromotion promotion in outcome.Applied)
        {
            explanation.Append(CultureInfo.InvariantCulture, $" {promotion.Code} ({promotion.Name}) took ")
                .Append(Format(promotion.DiscountAmount))
                .Append(" off");

            if (promotion.WasClamped)
            {
                explanation.Append(", capped so the line could not go below zero");
            }

            explanation.Append('.');
        }

        if (outcome.Applied.Count == 0)
        {
            explanation.Append(" No promotion applied.");
        }

        return explanation
            .Append(" Line total ")
            .Append(Format(netPayable))
            .Append('.')
            .ToString();
    }

    /// <summary>Formats money the way a person reads it — two places and a currency, not four.</summary>
    private static string Format(Money money)
        => string.Create(CultureInfo.InvariantCulture, $"{money.Currency} {money.Amount:0.00}");

    /// <summary>Formats a quantity without the six trailing zeros <c>decimal(18,6)</c> carries.</summary>
    private static string Trim(decimal quantity)
        => (quantity == decimal.Truncate(quantity) ? decimal.Truncate(quantity) : quantity)
            .ToString(CultureInfo.InvariantCulture);
}
