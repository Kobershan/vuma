using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>
/// The single most dispute-prone calculation in the module, isolated so it is testable without a
/// database: time-weighted benefit shares with largest-remainder dust (stage 10b rule 9).
/// </summary>
/// <remarks>
/// Weight: <c>w(m) = Σ amount × whole days held</c>, where days held is the payout date less the
/// contribution date (minimum 0) clipped to the member's active window
/// (<c>JoinedAt..LeftAt ?? AsAt</c>). Share: <c>B × w(m) / Σw</c>, remainder dust to the
/// highest-weight member by largest remainder. With no weight anywhere the pool splits equally —
/// a pool with nobody holding anything for any time has no time signal to follow.
/// </remarks>
public static class StokvelBenefitCalculator
{
    /// <summary>One member's time weight inputs.</summary>
    /// <param name="MemberId">The member.</param>
    /// <param name="JoinedAt">When they joined.</param>
    /// <param name="LeftAt">When they left, or null while active.</param>
    /// <param name="Contributions">Their (amount, paid-at) pairs.</param>
    public sealed record MemberWeightInput(
        Guid MemberId,
        DateTimeOffset JoinedAt,
        DateTimeOffset? LeftAt,
        IReadOnlyList<(decimal Amount, DateTimeOffset PaidAt)> Contributions);

    /// <summary>Splits a bonus pool across members, cent-exact.</summary>
    /// <param name="pool">The distributable pool. Must not be negative.</param>
    /// <param name="members">One entry per member sharing in it.</param>
    /// <param name="asAt">The payout date the weights are measured to, UTC.</param>
    /// <returns>One share per member, summing to the pool to the cent.</returns>
    public static IReadOnlyDictionary<Guid, Money> Split(
        Money pool,
        IReadOnlyList<MemberWeightInput> members,
        DateTimeOffset asAt)
    {
        ArgumentNullException.ThrowIfNull(members);

        if (pool.Amount < 0m)
        {
            throw new StokvelExceptions("STOKVEL_POOL_NEGATIVE", "The bonus pool may not be negative.");
        }

        var result = new Dictionary<Guid, Money>();
        if (members.Count == 0)
        {
            return result;
        }

        string currency = pool.Currency;
        decimal[] weights = new decimal[members.Count];
        for (int i = 0; i < members.Count; i++)
        {
            weights[i] = Weight(members[i], asAt);
        }

        decimal totalWeight = weights.Sum();
        decimal[] raw;
        if (totalWeight <= 0m)
        {
            raw = members.Select(_ => pool.Amount / members.Count).ToArray();
        }
        else
        {
            raw = weights.Select(w => pool.Amount * w / totalWeight).ToArray();
        }

        // Largest remainder to the cent: floor each share, hand the leftover cents to the
        // highest fractional parts (ties: highest weight first, then member order — deterministic).
        long[] floors = new long[members.Count];
        decimal[] fractions = new decimal[members.Count];
        for (int i = 0; i < raw.Length; i++)
        {
            decimal cents = raw[i] * 100m;
            long floor = (long)Math.Floor(cents);
            floors[i] = floor;
            fractions[i] = cents - floor;
        }

        long poolCents = (long)Math.Round(pool.Amount * 100m, MidpointRounding.AwayFromZero);
        long dust = poolCents - floors.Sum();
        int[] order = Enumerable.Range(0, members.Count)
            .OrderByDescending(i => fractions[i])
            .ThenByDescending(i => weights[i])
            .ThenBy(i => i)
            .ToArray();
        for (long k = 0; k < dust; k++)
        {
            floors[order[k % order.Length]]++;
        }

        for (int i = 0; i < members.Count; i++)
        {
            result[members[i].MemberId] = new Money(floors[i] / 100m, currency);
        }

        return result;
    }

    /// <summary>One member's time weight: Σ amount × whole days held inside the active window.</summary>
    public static decimal Weight(MemberWeightInput member, DateTimeOffset asAt)
    {
        ArgumentNullException.ThrowIfNull(member);

        DateTimeOffset end = member.LeftAt is { } left && left < asAt ? left : asAt;
        decimal weight = 0m;
        foreach ((decimal amount, DateTimeOffset paidAt) in member.Contributions)
        {
            DateTimeOffset start = paidAt > member.JoinedAt ? paidAt : member.JoinedAt;
            if (end <= start)
            {
                continue;
            }

            double days = (end - start).TotalDays;
            long whole = (long)Math.Floor(days);
            if (whole < 0)
            {
                whole = 0;
            }

            weight += amount * whole;
        }

        return weight;
    }

    /// <summary>
    /// Mid-cycle leaving pro-rata (rule 10): <c>refund = paid_in − spent_share − fee</c> where
    /// <c>spent_share = group_committed_spend × paid_in / total_paid_in</c>.
    /// </summary>
    public static Money LeavingRefund(Money paidIn, Money totalPaidIn, Money committedSpend, Money fee)
    {
        if (totalPaidIn.Amount <= 0m)
        {
            return Money.Zero(paidIn.Currency);
        }

        decimal share = committedSpend.Amount * paidIn.Amount / totalPaidIn.Amount;
        decimal refund = paidIn.Amount - share - fee.Amount;
        if (refund < 0m)
        {
            refund = 0m;
        }

        return new Money(refund, paidIn.Currency);
    }
}
