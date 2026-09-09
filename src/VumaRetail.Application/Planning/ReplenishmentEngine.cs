using VumaRetail.Domain.Planning;

namespace VumaRetail.Application.Planning;

/// <summary>One SKU/location the replenishment engine looks at.</summary>
/// <param name="ItemId">The item, or <c>null</c> for a variant.</param>
/// <param name="ItemVariantId">The variant, or <c>null</c> for an item.</param>
/// <param name="LocationId">The location that needs stock.</param>
/// <param name="Uom">The unit of measure.</param>
/// <param name="ReorderPoint">Units. At or below this, buy or move.</param>
/// <param name="SafetyStock">Units of buffer inside the reorder point.</param>
/// <param name="Available">Authoritative available-to-promise at the location.</param>
/// <param name="Incoming">Stock already inbound (open orders), in units.</param>
/// <param name="ForecastForHorizon">Forecast demand over lead time plus review period, in units.</param>
/// <param name="LeadTimeDays">Supplier lead time, for the requisition's required-by date.</param>
/// <param name="TransferCandidates">Fresh sister-company surpluses, best first. Empty when no link or no surplus.</param>
/// <param name="OverOpenToBuy">Whether raising would exceed open-to-buy — flagged, never blocking.</param>
public sealed record ReplenishmentInput(
    Guid? ItemId,
    Guid? ItemVariantId,
    Guid LocationId,
    string Uom,
    decimal ReorderPoint,
    decimal SafetyStock,
    decimal Available,
    decimal Incoming,
    decimal ForecastForHorizon,
    int LeadTimeDays,
    IReadOnlyList<TransferCandidate> TransferCandidates,
    bool OverOpenToBuy);

/// <summary>One sister-company surplus the link permits moving.</summary>
/// <param name="SourceCompanyId">The company holding it.</param>
/// <param name="SourceLocationId">The location holding it.</param>
/// <param name="Available">Fresh available there, in units.</param>
/// <param name="AsAt">When that figure was published.</param>
public sealed record TransferCandidate(
    Guid SourceCompanyId,
    Guid SourceLocationId,
    decimal Available,
    DateTimeOffset AsAt);

/// <summary>One proposed suggestion from the engine.</summary>
/// <param name="Reason">Why.</param>
/// <param name="Source">Where from.</param>
/// <param name="Quantity">How much.</param>
/// <param name="Candidate">The transfer source, when <paramref name="Source"/> is transfer.</param>
public sealed record ProposedSuggestion(
    SuggestionReason Reason,
    SuggestionSource Source,
    decimal Quantity,
    TransferCandidate? Candidate = null);

/// <summary>Pure replenishment proposer — decides, never commits.</summary>
public interface IReplenishmentEngine
{
    /// <summary>Proposes zero or more suggestions for one SKU/location.</summary>
    IReadOnlyList<ProposedSuggestion> Propose(ReplenishmentInput input);
}

/// <summary>
/// The replenishment rules (ADR-149): below the reorder point, buy (or move a linked surplus);
/// above it but short of the horizon forecast, top up to forecast plus safety stock. A transfer
/// surplus is preferred over procurement when it covers the need — one suggestion, never both.
/// </summary>
public sealed class ReplenishmentEngine : IReplenishmentEngine
{
    /// <inheritdoc />
    public IReadOnlyList<ProposedSuggestion> Propose(ReplenishmentInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        decimal cover = input.Available + input.Incoming;

        if (cover <= input.ReorderPoint)
        {
            decimal need = input.ReorderPoint - cover + input.SafetyStock;

            if (need <= 0m)
            {
                need = input.SafetyStock <= 0m ? 1m : input.SafetyStock;
            }

            return [PreferTransfer(input, need, SuggestionReason.BelowReorderPoint)];
        }

        decimal horizonNeed = input.ForecastForHorizon + input.SafetyStock - cover;

        if (horizonNeed > 0m)
        {
            return [PreferTransfer(input, horizonNeed, SuggestionReason.ForecastDrivenTopUp)];
        }

        return [];
    }

    private static ProposedSuggestion PreferTransfer(ReplenishmentInput input, decimal need, SuggestionReason reason)
    {
        // Deterministic: most available first, then lowest source location id. The candidates
        // arrive pre-filtered to fresh, linked surpluses — the engine never sees a stale figure.
        TransferCandidate? best = input.TransferCandidates
            .Where(candidate => candidate.Available > 0m)
            .OrderByDescending(candidate => candidate.Available)
            .ThenBy(candidate => candidate.SourceLocationId)
            .FirstOrDefault();

        if (best is not null && best.Available >= need)
        {
            return new ProposedSuggestion(SuggestionReason.TransferSurplus, SuggestionSource.Transfer, need, best);
        }

        return new ProposedSuggestion(reason, SuggestionSource.Procurement, need);
    }
}

