using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.CustomerAccounts;

/// <summary>
/// The single most dispute-prone calculation in the module: time-weighted benefit shares that a
/// member can recompute on a calculator, plus the pro-rata rule for leaving mid-cycle.
/// </summary>
public sealed class StokvelBenefitTests
{
    private static readonly DateTimeOffset AsAt = new(2026, 11, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Joined = AsAt.AddDays(-100);

    [Fact]
    public void Time_weighted_split_matches_the_hand_computed_fixture()
    {
        // Weights A 300,000 / B 150,000 / C 200,000 (amount × 100 days held each).
        var members = new List<StokvelBenefitCalculator.MemberWeightInput>
        {
            Input(Guid.NewGuid(), [(3000m, Joined)]),
            Input(Guid.NewGuid(), [(1500m, Joined)]),
            Input(Guid.NewGuid(), [(2000m, Joined)]),
        };

        IReadOnlyDictionary<Guid, Money> shares =
            StokvelBenefitCalculator.Split(new Money(300m, "ZAR"), members, AsAt);

        shares[members[0].MemberId].Amount.Should().Be(138.46m);
        shares[members[1].MemberId].Amount.Should().Be(69.23m);
        shares[members[2].MemberId].Amount.Should().Be(92.31m);
        shares.Values.Sum(m => m.Amount).Should().Be(300.00m);
    }

    [Fact]
    public void Dust_goes_to_the_highest_weight_member_by_largest_remainder()
    {
        // Equal weights: the remainder cent lands deterministically rather than vanishing.
        var members = new List<StokvelBenefitCalculator.MemberWeightInput>
        {
            Input(Guid.NewGuid(), [(1000m, Joined)]),
            Input(Guid.NewGuid(), [(1000m, Joined)]),
            Input(Guid.NewGuid(), [(1000m, Joined)]),
        };

        IReadOnlyDictionary<Guid, Money> shares =
            StokvelBenefitCalculator.Split(new Money(100m, "ZAR"), members, AsAt);

        shares.Values.Sum(m => m.Amount).Should().Be(100.00m);
        shares.Values.Select(m => m.Amount).OrderDescending().First().Should().Be(33.34m);
    }

    [Fact]
    public void Leaver_forfeits_unvested_benefits_pro_rata()
    {
        // Paid R10,000 (A 6,000 / B 4,000), committed R1,000: A's spent share is R600,
        // so the refund is R5,400 with no fee in play.
        Money refund = StokvelBenefitCalculator.LeavingRefund(
            new Money(6000m, "ZAR"), new Money(10000m, "ZAR"),
            new Money(1000m, "ZAR"), Money.Zero("ZAR"));

        refund.Amount.Should().Be(5400m);

        // And the fee is honoured when one applies: R6,000 − R600 − R100 = R5,300.
        Money withFee = StokvelBenefitCalculator.LeavingRefund(
            new Money(6000m, "ZAR"), new Money(10000m, "ZAR"),
            new Money(1000m, "ZAR"), new Money(100m, "ZAR"));

        withFee.Amount.Should().Be(5300m);
    }

    [Fact]
    public void Leaving_freezes_vesting_at_the_door()
    {
        Guid memberId = Guid.NewGuid();
        var member = StokvelMember.Join(
            Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(),
            MemberRole.Member, new Money(500m, "ZAR"), Joined);

        DateTimeOffset leftAt = Joined.AddDays(40);
        member.Leave(leftAt);

        member.IsActive.Should().BeFalse();
        member.IsActiveAt(Joined.AddDays(39)).Should().BeTrue();
        member.IsActiveAt(leftAt).Should().BeFalse();

        // 100 days of holding, but only 40 inside the active window.
        var input = new StokvelBenefitCalculator.MemberWeightInput(
            memberId, Joined, leftAt, [(1000m, Joined)]);
        StokvelBenefitCalculator.Weight(input, AsAt).Should().Be(40000m);
    }

    private static StokvelBenefitCalculator.MemberWeightInput Input(
        Guid memberId, IReadOnlyList<(decimal Amount, DateTimeOffset PaidAt)> contributions)
        => new(memberId, Joined, null, contributions);
}
