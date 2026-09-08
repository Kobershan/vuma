using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry.Trading;

/// <summary>
/// Splits one captured tender across company segments (ADR-126).
/// </summary>
/// <remarks>
/// Default is proportional by segment gross. Money has no fractional cent, so the split is
/// computed floor-first and the remainder dust goes deterministically to the largest segment
/// (ties broken by lowest company id — byte-order stable, so the same basket allocates the
/// same way on every run and every machine). A cashier may override with any exact split;
/// proportional is only the default. The rule is stated on the result because the person
/// reading it is a cashier reconciling a drawer, not a developer.
/// </remarks>
public static class TenderAllocator
{
    /// <summary>Allocates <paramref name="tender"/> across <paramref name="segments"/>.</summary>
    /// <param name="tender">The captured tender. Must be non-negative.</param>
    /// <param name="segments">One gross per company, in any order. Must be non-empty.</param>
    /// <returns>One allocation per segment, summing to the tender exactly, plus the stated basis.</returns>
    /// <exception cref="TradingSessionException">No segments, or the tender is negative.</exception>
    public static TenderAllocationResult AllocateDefault(
        Money tender, IReadOnlyList<(Guid CompanyId, Money Gross)> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Count == 0)
        {
            throw new TradingSessionException(
                "TRADING_NO_SEGMENTS", "A tender cannot be allocated across zero segments.");
        }

        if (tender.IsNegative)
        {
            throw new TradingSessionException(
                "TRADING_NEGATIVE_TENDER", "A tender cannot be negative.");
        }

        string currency = tender.Currency;
        foreach ((Guid _, Money gross) in segments)
        {
            if (!string.Equals(gross.Currency, currency, StringComparison.Ordinal))
            {
                throw TradingSessionException.CurrencyMismatch(currency, gross.Currency);
            }
        }

        decimal total = segments.Sum(segment => segment.Gross.Amount);
        if (total <= 0m)
        {
            throw new TradingSessionException(
                "TRADING_ZERO_BASKET", "A tender cannot be allocated across a zero-gross basket.");
        }

        // Floor-first in cents: every segment gets its truncated share, then leftover
        // whole cents go one each, largest segment first (ties: lowest company id —
        // byte-order stable, so the same basket allocates the same way on every run and
        // every machine). The loop is bounded: dust is always fewer cents than segments,
        // so one pass suffices, but the modulo keeps it exact under any rounding.
        int[] floorCents = new int[segments.Count];
        for (int index = 0; index < segments.Count; index++)
        {
            decimal exact = tender.Amount * segments[index].Gross.Amount / total;
            floorCents[index] = (int)decimal.Floor(exact * 100m);
        }

        int targetCents = (int)decimal.Round(tender.Amount * 100m, MidpointRounding.AwayFromZero);
        int dust = targetCents - floorCents.Sum();

        int[] order = segments
            .Select((segment, index) => index)
            .OrderByDescending(index => segments[index].Gross.Amount)
            .ThenBy(index => segments[index].CompanyId)
            .ToArray();

        for (int cent = 0; cent < dust; cent++)
        {
            floorCents[order[cent % order.Length]]++;
        }

        List<(Guid CompanyId, Money Amount)> allocations = new(segments.Count);
        for (int index = 0; index < segments.Count; index++)
        {
            allocations.Add((segments[index].CompanyId, new Money(floorCents[index] / 100m, currency)));
        }

        Guid dustCompany = segments[order[0]].CompanyId;
        string basis = dust > 0
            ? $"proportional, dust {new Money(dust / 100m, currency).Amount:F2} {currency} to {dustCompany}"
            : "proportional, exact";

        return new TenderAllocationResult(allocations, basis);
    }

    /// <summary>Validates a cashier override: same companies, exact sum, no negatives.</summary>
    /// <exception cref="TradingSessionException">The override does not sum to the tender exactly.</exception>
    public static TenderAllocationResult AllocateOverride(
        Money tender,
        IReadOnlyList<(Guid CompanyId, Money Gross)> segments,
        IReadOnlyList<(Guid CompanyId, Money Amount)> overrides)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(overrides);

        HashSet<Guid> segmentCompanies = [.. segments.Select(segment => segment.CompanyId)];
        HashSet<Guid> overrideCompanies = [.. overrides.Select(entry => entry.CompanyId)];

        if (!segmentCompanies.SetEquals(overrideCompanies))
        {
            throw new TradingSessionException(
                "TRADING_ALLOCATION_COMPANIES",
                "An allocation override must name exactly the basket's companies, once each.");
        }

        foreach ((Guid _, Money amount) in overrides)
        {
            if (!string.Equals(amount.Currency, tender.Currency, StringComparison.Ordinal))
            {
                throw TradingSessionException.CurrencyMismatch(tender.Currency, amount.Currency);
            }

            if (amount.IsNegative)
            {
                throw new TradingSessionException(
                    "TRADING_NEGATIVE_ALLOCATION", "A segment allocation cannot be negative.");
            }
        }

        Money total = overrides.Aggregate(Money.Zero(tender.Currency), (sum, entry) => sum + entry.Amount);
        if (total != tender)
        {
            throw TradingSessionException.AllocationMismatch(total.Amount, tender.Amount, tender.Currency);
        }

        return new TenderAllocationResult([.. overrides], "cashier override, exact");
    }
}

/// <summary>One allocation per segment plus the stated basis.</summary>
/// <param name="Allocations">The per-company amounts, summing to the tender exactly.</param>
/// <param name="Basis">The rule, stated for the cashier reconciling the drawer.</param>
public sealed record TenderAllocationResult(
    IReadOnlyList<(Guid CompanyId, Money Amount)> Allocations, string Basis);
