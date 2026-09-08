using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.CustomerAccounts;

/// <summary>
/// Liability reconciliation at volume: five thousand member rows project to the cent, and the
/// per-member positions sum to the group figure. The GL control-account leg of the same proof
/// runs in the seed and the hosted passes; what is asserted here is that the projection the
/// ledger would reconcile against is itself exact.
/// </summary>
public sealed class StokvelReconciliationTests
{
    [Fact]
    public void Five_thousand_transactions_reconcile_to_the_cent()
    {
        const string currency = "ZAR";
        Guid groupId = UuidV7.NewGuid();
        var members = new[] { UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid() };
        var contributions = new List<StokvelContribution>(5000);
        var benefits = new List<StokvelBenefitAllocation>();
        var payouts = new List<StokvelPayout>();

        DateTimeOffset start = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
        decimal expectedPaid = 0m;
        for (int i = 0; i < 5000; i++)
        {
            Guid memberId = members[i % 3];
            // Two-decimal amounts only: till money, never a float.
            decimal amount = ((i % 50) + 1) * 1.11m;
            expectedPaid += amount;
            contributions.Add(StokvelContribution.Record(
                UuidV7.NewGuid(), null, groupId, memberId, new Money(amount, currency),
                $"RCPT-{i:00000}", start.AddHours(i), i % 2 == 0 ? "Till" : "EFT"));
        }

        DateTimeOffset asAt = start.AddHours(5000);
        var inputs = members.Select(memberId => new StokvelBenefitCalculator.MemberWeightInput(
            memberId, start, null,
            contributions
                .Where(c => c.MemberId == memberId)
                .Select(c => (c.Amount.Amount, c.PaidAt))
                .ToList())).ToList();
        IReadOnlyDictionary<Guid, Money> shares =
            StokvelBenefitCalculator.Split(new Money(900m, currency), inputs, asAt);
        foreach (var memberId in members)
        {
            benefits.Add(StokvelBenefitAllocation.Allocate(
                UuidV7.NewGuid(), null, groupId, memberId, shares[memberId],
                "time-weighted 2026 cycle", asAt));
        }

        var settled = StokvelPayout.Request(
            UuidV7.NewGuid(), null, groupId, members[0],
            StokvelPayoutKind.Cash, new Money(250m, currency), null, asAt);
        settled.Approve(asAt);
        settled.Settle(asAt.AddMinutes(5));
        payouts.Add(settled);

        var group = StokvelGroup.Create(
            UuidV7.NewGuid(), null, "STK-REC", "Grocery", StokvelType.GroceryHamper,
            "constitution", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15),
            UuidV7.NewGuid());

        Money projected = group.Balance(contributions, payouts, benefits, currency);

        decimal expected = expectedPaid + 900m - 250m;
        projected.Amount.Should().Be(expected);

        // Every member position sums to the group figure: no rand leaks between the two reads.
        decimal memberSum = 0m;
        foreach (var memberId in members)
        {
            Money available = new StokvelMemberShim(memberId).Available(
                contributions.Where(c => c.MemberId == memberId),
                payouts.Where(p => p.MemberId == memberId),
                benefits.Where(b => b.MemberId == memberId),
                currency);
            memberSum += available.Amount;
        }

        memberSum.Should().Be(projected.Amount);
    }

    /// <summary>Member balance math without a member row: the projection is pure sums.</summary>
    private sealed class StokvelMemberShim(Guid memberId)
    {
        public Money Available(
            IEnumerable<StokvelContribution> contributions,
            IEnumerable<StokvelPayout> payouts,
            IEnumerable<StokvelBenefitAllocation> benefits,
            string currency)
        {
            Money total = Money.Zero(currency);
            foreach (var c in contributions)
            {
                total += c.Amount;
            }

            foreach (var b in benefits)
            {
                total += b.Amount;
            }

            foreach (var p in payouts.Where(p => p.Status == StokvelPayoutStatus.Settled))
            {
                total -= p.Amount;
            }

            _ = memberId;
            return total;
        }
    }
}
