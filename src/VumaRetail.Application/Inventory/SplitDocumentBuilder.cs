using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Inventory;

/// <summary>
/// Turns a committed sourcing plan into one draft per supplying company, asserting the split
/// reconciles line for line and cent for cent.
/// </summary>
/// <remarks>
/// Pure: no database, no clock, no numbering (each company's series numbers its own segment at
/// write time). Money slices telescope across each demand line's allocations in a deterministic
/// order (company, then location), so partial slices sum exactly to the covered share and the
/// backorder takes the residual — the same cumulative-rounding shape Stage 10's sales returns
/// use (ADR-075). The assert then checks plumbing, not arithmetic philosophy: every source line
/// lands on exactly the drafts it should, quantities balance, money is exact and currency-clean.
/// </remarks>
public sealed class SplitDocumentBuilder : ISplitDocumentBuilder
{
    /// <inheritdoc />
    public IReadOnlyList<SplitOrderDraft> Build(SourcingSourceOrder source, SourcingPlan committed)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(committed);

        if (source.Demands.Count == 0)
        {
            throw new ArgumentException("A split needs at least one demand line.", nameof(source));
        }

        string currency = source.Currency;
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("The source order must name its currency.", nameof(source));
        }

        Dictionary<Guid, SourcingDemandLine> demands = source.Demands.ToDictionary(line => line.LineId);
        if (demands.Count != source.Demands.Count)
        {
            throw new ArgumentException("Demand line ids must be unique.", nameof(source));
        }

        foreach (SourcingDemandLine demand in source.Demands)
        {
            EnsureSnapshotCoherent(demand, currency);
        }

        Dictionary<Guid, List<SplitOrderLineDraft>> byCompany = [];
        Dictionary<Guid, Guid> fulfillingLocation = [];

        foreach (SourcingPlanLine planLine in committed.Lines)
        {
            if (!demands.TryGetValue(planLine.LineId, out SourcingDemandLine? demand))
            {
                throw InventoryRuleException.SplitReconciliationMismatch(
                    $"the plan covers line {planLine.LineId}, which the source order does not have.");
            }

            List<SourcingAllocation> slices = [.. planLine.Allocations
                .OrderBy(allocation => allocation.CompanyId)
                .ThenBy(allocation => allocation.LocationId)];

            if (slices.Count == 0)
            {
                // Fully backordered: nothing held, so no segment carries this line. The plan's
                // backorder already accounts for the whole demand; the assert below re-checks that.
                continue;
            }

            List<decimal> sliceQuantities = slices.Select(slice => slice.Quantity.Value).ToList();

            // The whole-line discount is what the unit price did not cover; both it and the tax
            // telescope across the slices, while the unit price rides whole (per-unit needs no split).
            Money discountWhole = (demand.UnitPrice * demand.Demanded.Value) - demand.LineNet;
            IReadOnlyList<Money> discountShares = MoneyTelescoping.Split(
                discountWhole, demand.Demanded.Value, sliceQuantities);
            IReadOnlyList<Money> taxShares = MoneyTelescoping.Split(
                demand.LineTax, demand.Demanded.Value, sliceQuantities);

            for (int index = 0; index < slices.Count; index++)
            {
                SourcingAllocation slice = slices[index];

                if (!byCompany.TryGetValue(slice.CompanyId, out List<SplitOrderLineDraft>? lines))
                {
                    lines = [];
                    byCompany[slice.CompanyId] = lines;
                }

                lines.Add(new SplitOrderLineDraft(
                    demand.LineId,
                    demand.ItemId,
                    demand.ItemVariantId,
                    slice.Quantity,
                    demand.UnitPrice,
                    discountShares[index],
                    taxShares[index]));

                // The fulfilling location is the company's largest allocation: deterministic, and the
                // place most of the segment ships from.
                if (!fulfillingLocation.TryGetValue(slice.CompanyId, out Guid current)
                    || slice.Quantity.Value > slices
                        .Where(candidate => candidate.LocationId == current)
                        .Sum(candidate => candidate.Quantity.Value))
                {
                    fulfillingLocation[slice.CompanyId] = slice.LocationId;
                }
            }
        }

        List<SplitOrderDraft> drafts = byCompany
            .OrderBy(entry => entry.Key)
            .Select(entry => new SplitOrderDraft(entry.Key, fulfillingLocation[entry.Key], entry.Value))
            .ToList();

        AssertReconciles(source, committed, drafts);

        return drafts;
    }

    private static void EnsureSnapshotCoherent(SourcingDemandLine demand, string currency)
    {
        foreach (Money amount in new[] { demand.UnitPrice, demand.LineNet, demand.LineTax, demand.LineGross })
        {
            if (!string.Equals(amount.Currency, currency, StringComparison.Ordinal))
            {
                throw new InventoryRuleException(
                    "INVENTORY_SPLIT_CURRENCY_MISMATCH",
                    $"Line {demand.LineId} carries {amount.Currency} against an order in {currency}. "
                    + "One order, one currency.");
            }
        }

        if ((demand.LineNet + demand.LineTax).Amount != demand.LineGross.Amount)
        {
            throw InventoryRuleException.SplitReconciliationMismatch(
                $"line {demand.LineId} carries net {demand.LineNet.Amount} plus tax {demand.LineTax.Amount} "
                + $"against gross {demand.LineGross.Amount}. A snapshot that does not add up cannot split.");
        }
    }

    private static void AssertReconciles(
        SourcingSourceOrder source,
        SourcingPlan committed,
        IReadOnlyList<SplitOrderDraft> drafts)
    {
        Dictionary<Guid, SourcingDemandLine> demands = source.Demands.ToDictionary(line => line.LineId);
        Dictionary<Guid, SourcingPlanLine> planLines = committed.Lines.ToDictionary(line => line.LineId);

        if (planLines.Count != demands.Count)
        {
            throw InventoryRuleException.SplitReconciliationMismatch(
                $"the plan covers {planLines.Count} lines against {demands.Count} demanded.");
        }

        var sliced = drafts
            .SelectMany(draft => draft.Lines.Select(line => (draft.CompanyId, line)))
            .ToList();

        HashSet<(Guid Company, Guid Line)> seen = [];
        foreach ((Guid company, SplitOrderLineDraft line) in sliced)
        {
            if (!seen.Add((company, line.SourceLineId)))
            {
                throw InventoryRuleException.SplitReconciliationMismatch(
                    $"line {line.SourceLineId} lands twice on company {company}'s segment.");
            }

            if (!demands.ContainsKey(line.SourceLineId))
            {
                throw InventoryRuleException.SplitReconciliationMismatch(
                    $"line {line.SourceLineId} is on a segment but on no source line.");
            }

            if (!string.Equals(line.Quantity.UnitOfMeasure, demands[line.SourceLineId].Demanded.UnitOfMeasure, StringComparison.Ordinal))
            {
                throw InventoryRuleException.SplitReconciliationMismatch(
                    $"line {line.SourceLineId} is split in {line.Quantity.UnitOfMeasure} "
                    + $"against {demands[line.SourceLineId].Demanded.UnitOfMeasure} demanded.");
            }
        }

        foreach (SourcingDemandLine demand in source.Demands)
        {
            SourcingPlanLine planLine = planLines[demand.LineId];
            List<SplitOrderLineDraft> slices = sliced
                .Where(entry => entry.line.SourceLineId == demand.LineId)
                .Select(entry => entry.line)
                .ToList();

            decimal slicedQuantity = slices.Sum(line => line.Quantity.Value);
            decimal plannedQuantity = planLine.Allocations.Sum(allocation => allocation.Quantity.Value);
            if (slicedQuantity != plannedQuantity
                || slicedQuantity + planLine.Backorder.Value != demand.Demanded.Value)
            {
                throw InventoryRuleException.SplitReconciliationMismatch(
                    $"line {demand.LineId} demands {demand.Demanded.Value}, "
                    + $"plans {plannedQuantity}, backorders {planLine.Backorder.Value}, "
                    + $"but segments carry {slicedQuantity}.");
            }

            // Re-derived from the plan (not from the drafts): the telescoped covered share. Any
            // other total is a plumbing error — a dropped slice, a double-counted line, a currency
            // mixup — not a rounding choice.
            decimal expectedDiscount = TelescopedShare(
                demand,
                planLine,
                (demand.UnitPrice * demand.Demanded.Value) - demand.LineNet);
            decimal expectedTax = TelescopedShare(demand, planLine, demand.LineTax);

            decimal slicedDiscount = slices.Sum(line => line.DiscountAmount.Amount);
            decimal slicedTax = slices.Sum(line => line.TaxAmount.Amount);

            if (slicedDiscount != expectedDiscount || slicedTax != expectedTax)
            {
                throw InventoryRuleException.SplitReconciliationMismatch(
                    $"line {demand.LineId} segments carry discount {slicedDiscount} / tax {slicedTax} "
                    + $"against {expectedDiscount} / {expectedTax}.");
            }
        }
    }

    private static decimal TelescopedShare(SourcingDemandLine demand, SourcingPlanLine planLine, Money whole)
    {
        List<decimal> sliceQuantities = planLine.Allocations
            .OrderBy(allocation => allocation.CompanyId)
            .ThenBy(allocation => allocation.LocationId)
            .Select(allocation => allocation.Quantity.Value)
            .ToList();

        if (sliceQuantities.Count == 0)
        {
            return 0m;
        }

        IReadOnlyList<Money> shares = MoneyTelescoping.Split(whole, demand.Demanded.Value, sliceQuantities);

        return shares.Sum(share => share.Amount);
    }
}
