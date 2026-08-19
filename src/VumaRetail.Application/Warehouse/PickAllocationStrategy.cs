using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Application.Warehouse;

/// <summary>One bin allocated to satisfy part or all of a pick line's demand.</summary>
/// <param name="BinId">The bin.</param>
/// <param name="Quantity">How much this bin can contribute.</param>
public sealed record BinAllocation(Guid BinId, Quantity Quantity);

/// <summary>
/// Given a pick line's demand at a location, decides which bin(s) to allocate it from.
/// </summary>
/// <remarks>
/// Not a claim of optimality (see the stage document's "what this stage does not own") — a working,
/// testable allocator behind a seam a later stage's slotting/wave-optimisation engine can replace
/// without touching anything else.
/// </remarks>
public interface IPickAllocationStrategy
{
    /// <summary>
    /// Allocates <paramref name="requested"/> across one or more bins from <paramref name="candidates"/>,
    /// most-available-first, until the demand is satisfied or the candidates are exhausted.
    /// </summary>
    /// <param name="requested">How much is needed.</param>
    /// <param name="candidates">The bins holding this stock-keeping unit at the location, in any order.</param>
    /// <returns>
    /// One or more allocations summing to at most <paramref name="requested"/>. Empty if no candidate
    /// holds any of this stock-keeping unit.
    /// </returns>
    IReadOnlyList<BinAllocation> Allocate(Quantity requested, IReadOnlyList<BinStock> candidates);
}

/// <summary>
/// Allocates from the bin holding the most of a stock-keeping unit first, spilling into the next
/// largest only if the first cannot fully satisfy the demand. ADR-090.
/// </summary>
/// <remarks>
/// Minimises how many bins a picker has to visit for one line, which is the single easiest win for a
/// hand-pushed cart with no routing optimisation behind it — a real slotting engine (Stage 15/24
/// territory) can replace this without any caller needing to change.
/// </remarks>
public sealed class LargestBinFirstAllocationStrategy : IPickAllocationStrategy
{
    /// <inheritdoc />
    public IReadOnlyList<BinAllocation> Allocate(Quantity requested, IReadOnlyList<BinStock> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (requested.IsNegative || requested.IsZero)
        {
            throw WarehouseRuleException.QuantityMustBePositive();
        }

        List<BinAllocation> allocations = [];
        Quantity remaining = requested;

        foreach (BinStock candidate in candidates.OrderByDescending(c => c.QuantityOnHand.Value))
        {
            if (remaining.IsZero)
            {
                break;
            }

            if (candidate.QuantityOnHand.IsZero)
            {
                continue;
            }

            Quantity take = candidate.QuantityOnHand >= remaining ? remaining : candidate.QuantityOnHand;
            allocations.Add(new BinAllocation(candidate.BinId, take));
            remaining -= take;
        }

        return allocations;
    }
}
