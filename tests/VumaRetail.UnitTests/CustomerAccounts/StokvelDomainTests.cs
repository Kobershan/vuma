using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.CustomerAccounts;

/// <summary>
/// Stokvel aggregate guards: every factory refuses its own nonsense with a code, and status
/// transitions only move forward. The handlers rely on these rather than re-checking them.
/// </summary>
public sealed class StokvelDomainTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Group_guards_its_cycle_and_status_lifecycle()
    {
        Action inverted = () => StokvelGroup.Create(
            TenantId, StoreId, "STK-1", "Grocery", StokvelType.Savings,
            "constitution", new DateOnly(2026, 12, 15), new DateOnly(2026, 1, 1),
            StoreId);
        inverted.Should().Throw<StokvelExceptions>();

        Action noTenant = () => StokvelGroup.Create(
            Guid.Empty, StoreId, "STK-1", "Grocery", StokvelType.Savings,
            "constitution", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15),
            StoreId);
        noTenant.Should().Throw<ArgumentException>();

        var group = StokvelGroup.Create(
            TenantId, StoreId, "STK-1", "Grocery", StokvelType.Savings,
            "constitution", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15),
            StoreId);
        Action payoutBeforeActive = () => group.BeginPayout();
        payoutBeforeActive.Should().Throw<StokvelExceptions>();

        group.Activate();
        group.BeginPayout();
        group.Close();
        Action closedAgain = () => group.Close();
        closedAgain.Should().Throw<StokvelExceptions>();
    }

    [Fact]
    public void Member_join_and_leave_guards()
    {
        Guid groupId = UuidV7.NewGuid();

        Action negative = () => StokvelMember.Join(
            TenantId, StoreId, groupId, UuidV7.NewGuid(),
            MemberRole.Member, new Money(-1m, "ZAR"), Now);
        negative.Should().Throw<StokvelExceptions>();

        Action noPartner = () => StokvelMember.Join(
            TenantId, StoreId, groupId, Guid.Empty,
            MemberRole.Member, new Money(500m, "ZAR"), Now);
        noPartner.Should().Throw<ArgumentException>();

        var member = StokvelMember.Join(
            TenantId, StoreId, groupId, UuidV7.NewGuid(),
            MemberRole.Member, new Money(500m, "ZAR"), Now);
        member.IsActive.Should().BeTrue();
        member.Leave(Now);
        Action twice = () => member.Leave(Now);
        twice.Should().Throw<StokvelExceptions>();
    }

    [Fact]
    public void Contribution_and_benefit_guards()
    {
        Guid groupId = UuidV7.NewGuid();
        Guid memberId = UuidV7.NewGuid();

        Action zero = () => StokvelContribution.Record(
            TenantId, StoreId, groupId, memberId, Money.Zero("ZAR"),
            "RCPT-1", Now, "Till");
        zero.Should().Throw<StokvelExceptions>();

        Action noReceipt = () => StokvelContribution.Record(
            TenantId, StoreId, groupId, memberId, new Money(100m, "ZAR"),
            "", Now, "Till");
        noReceipt.Should().Throw<ArgumentException>();

        Action negativeBenefit = () => StokvelBenefitAllocation.Allocate(
            TenantId, StoreId, groupId, memberId, new Money(-1m, "ZAR"),
            "basis", Now);
        negativeBenefit.Should().Throw<StokvelExceptions>();

        Action noBasis = () => StokvelBenefitAllocation.Allocate(
            TenantId, StoreId, groupId, memberId, Money.Zero("ZAR"),
            "", Now);
        noBasis.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Payout_request_approve_settle_guards()
    {
        Guid groupId = UuidV7.NewGuid();
        Guid memberId = UuidV7.NewGuid();

        Action zero = () => StokvelPayout.Request(
            TenantId, StoreId, groupId, memberId, StokvelPayoutKind.Cash,
            Money.Zero("ZAR"), null, Now);
        zero.Should().Throw<StokvelExceptions>();

        Action hamperWithoutBasket = () => StokvelPayout.Request(
            TenantId, StoreId, groupId, memberId, StokvelPayoutKind.Hamper,
            new Money(450m, "ZAR"), null, Now);
        hamperWithoutBasket.Should().Throw<StokvelExceptions>();

        var payout = StokvelPayout.Request(
            TenantId, StoreId, groupId, memberId, StokvelPayoutKind.Cash,
            new Money(100m, "ZAR"), null, Now);
        Action settleBeforeApprove = () => payout.Settle(Now);
        settleBeforeApprove.Should().Throw<StokvelExceptions>();

        payout.Approve(Now);
        Action twice = () => payout.Approve(Now);
        twice.Should().Throw<StokvelExceptions>();

        var goods = StokvelPayout.Request(
            TenantId, StoreId, groupId, memberId, StokvelPayoutKind.Goods,
            new Money(100m, "ZAR"), UuidV7.NewGuid(), Now);
        goods.Approve(Now);
        Action goodsWithoutSale = () => goods.Settle(Now);
        goodsWithoutSale.Should().Throw<StokvelExceptions>();

        payout.Settle(Now);
        payout.Status.Should().Be(StokvelPayoutStatus.Settled);
    }

    [Fact]
    public void Hamper_basket_guards()
    {
        Guid groupId = UuidV7.NewGuid();

        Action zeroPrice = () => HamperBasket.Create(
            TenantId, StoreId, groupId, "Hamper", Money.Zero("ZAR"),
            new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 24), UuidV7.NewGuid());
        zeroPrice.Should().Throw<StokvelExceptions>();

        Action invertedSeason = () => HamperBasket.Create(
            TenantId, StoreId, groupId, "Hamper", new Money(450m, "ZAR"),
            new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 1), UuidV7.NewGuid());
        invertedSeason.Should().Throw<StokvelExceptions>();

        Action noLocation = () => HamperBasket.Create(
            TenantId, StoreId, groupId, "Hamper", new Money(450m, "ZAR"),
            new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 24), Guid.Empty);
        noLocation.Should().Throw<ArgumentException>();

        var basket = HamperBasket.Create(
            TenantId, StoreId, groupId, "Hamper", new Money(450m, "ZAR"),
            new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 24), UuidV7.NewGuid());

        Action bothIds = () => HamperBasketLine.Create(
            TenantId, StoreId, basket.Id, ItemId, UuidV7.NewGuid(), 2m, "EA");
        bothIds.Should().Throw<StokvelExceptions>();

        Action neitherId = () => HamperBasketLine.Create(
            TenantId, StoreId, basket.Id, null, null, 2m, "EA");
        neitherId.Should().Throw<StokvelExceptions>();

        Action zeroQty = () => HamperBasketLine.Create(
            TenantId, StoreId, basket.Id, ItemId, null, 0m, "EA");
        zeroQty.Should().Throw<StokvelExceptions>();

        Action bothSubstitutes = () => HamperBasketLine.Create(
            TenantId, StoreId, basket.Id, ItemId, null, 2m, "EA",
            UuidV7.NewGuid(), UuidV7.NewGuid());
        bothSubstitutes.Should().Throw<StokvelExceptions>();
    }

    [Fact]
    public void Group_balance_is_a_projection_not_a_column()
    {
        Guid groupId = UuidV7.NewGuid();
        Guid alice = UuidV7.NewGuid();
        Guid bob = UuidV7.NewGuid();
        var group = StokvelGroup.Create(
            TenantId, StoreId, "STK-2", "Grocery", StokvelType.Savings,
            "constitution", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15),
            StoreId);

        var contributions = new List<StokvelContribution>
        {
            StokvelContribution.Record(
                TenantId, StoreId, groupId, alice, new Money(500m, "ZAR"),
                "RCPT-A", Now, "Till"),
            StokvelContribution.Record(
                TenantId, StoreId, groupId, bob, new Money(300m, "ZAR"),
                "RCPT-B", Now, "Till"),
        };
        var benefits = new List<StokvelBenefitAllocation>
        {
            StokvelBenefitAllocation.Allocate(
                TenantId, StoreId, groupId, alice, new Money(50m, "ZAR"),
                "time-weighted", Now),
        };
        var payout = StokvelPayout.Request(
            TenantId, StoreId, groupId, bob, StokvelPayoutKind.Cash,
            new Money(100m, "ZAR"), null, Now);
        var requested = StokvelPayout.Request(
            TenantId, StoreId, groupId, bob, StokvelPayoutKind.Cash,
            new Money(1000m, "ZAR"), null, Now);

        // Requested-but-unsettled payouts move nothing: 500 + 300 + 50 − 0.
        group.Balance(contributions, [requested], benefits, "ZAR").Amount.Should().Be(850m);

        payout.Approve(Now);
        payout.Settle(Now);
        group.Balance(contributions, [payout], benefits, "ZAR").Amount.Should().Be(750m);
    }
}
