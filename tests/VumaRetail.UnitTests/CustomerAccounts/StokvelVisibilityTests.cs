using NSubstitute;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.CustomerAccounts.Queries;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.CustomerAccounts;

/// <summary>
/// The governance wall: a member reads only their own line, while the treasurer, chair and
/// secretary read the group. Declaring the wall in the constitution is not enforcing it.
/// </summary>
public sealed class StokvelVisibilityTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Member_sees_only_their_own_line()
    {
        Wall wall = Wall.Private();

        // Member A asking for B's rows is refused with the coded wall exception.
        Func<Task> snooping = () => wall.Members.HandleAsync(
            new GetMemberStatementQuery(wall.Group.Id, wall.Bob.Id, wall.Alice.PartnerId, MemberRole.Member));

        await snooping.Should().ThrowAsync<StokvelVisibilityException>();

        // Their own rows pass.
        MemberStatementResult own = await wall.Members.HandleAsync(
            new GetMemberStatementQuery(wall.Group.Id, wall.Alice.Id, wall.Alice.PartnerId, MemberRole.Member));

        own.MemberId.Should().Be(wall.Alice.Id);
        own.Available.Amount.Should().Be(500m);
    }

    [Fact]
    public async Task Constitution_may_open_member_reading()
    {
        Wall wall = Wall.Open();

        MemberStatementResult other = await wall.Members.HandleAsync(
            new GetMemberStatementQuery(wall.Group.Id, wall.Bob.Id, wall.Alice.PartnerId, MemberRole.Member));

        other.MemberId.Should().Be(wall.Bob.Id);
    }

    [Theory]
    [InlineData(MemberRole.Treasurer)]
    [InlineData(MemberRole.Chairperson)]
    [InlineData(MemberRole.Secretary)]
    public async Task Treasurer_chair_secretary_see_the_group(MemberRole role)
    {
        Wall wall = Wall.Private();

        GroupStatementResult group = await wall.Groups.HandleAsync(
            new GetGroupStatementQuery(wall.Group.Id, role));

        group.Members.Should().HaveCount(2);
        group.GroupBalance.Amount.Should().Be(1000m);
    }

    [Fact]
    public async Task Plain_member_does_not_see_the_group()
    {
        Wall wall = Wall.Private();

        Func<Task> snooping = () => wall.Groups.HandleAsync(
            new GetGroupStatementQuery(wall.Group.Id, MemberRole.Member));

        await snooping.Should().ThrowAsync<StokvelVisibilityException>();
    }

    private sealed class Wall
    {
        public StokvelGroup Group { get; }
        public StokvelMember Alice { get; }
        public StokvelMember Bob { get; }
        public GetMemberStatementQueryHandler Members { get; }
        public GetGroupStatementQueryHandler Groups { get; }

        private Wall(string constitution)
        {
            Group = StokvelGroup.Create(
                TenantId, StoreId, $"STK-{Guid.NewGuid():N}", "Grocery", StokvelType.GroceryHamper,
                constitution, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15),
                StoreId, UuidV7.NewGuid());
            Alice = StokvelMember.Join(
                TenantId, StoreId, Group.Id, UuidV7.NewGuid(),
                MemberRole.Member, new Money(500m, "ZAR"), Now);
            Bob = StokvelMember.Join(
                TenantId, StoreId, Group.Id, UuidV7.NewGuid(),
                MemberRole.Treasurer, new Money(500m, "ZAR"), Now);

            var groups = Substitute.For<IStokvelGroupRepository>();
            groups.FindAsync(Group.Id, Arg.Any<CancellationToken>()).Returns(Group);
            groups.FindMemberAsync(Alice.Id, Arg.Any<CancellationToken>()).Returns(Alice);
            groups.FindMemberAsync(Bob.Id, Arg.Any<CancellationToken>()).Returns(Bob);
            groups.ListMembersAsync(Group.Id, Arg.Any<CancellationToken>())
                .Returns(new List<StokvelMember> { Alice, Bob });

            var contributions = new List<StokvelContribution>
            {
                StokvelContribution.Record(
                    TenantId, StoreId, Group.Id, Alice.Id, new Money(500m, "ZAR"),
                    "RCPT-A", Now, "Till"),
                StokvelContribution.Record(
                    TenantId, StoreId, Group.Id, Bob.Id, new Money(500m, "ZAR"),
                    "RCPT-B", Now, "Till"),
            };
            var ledger = Substitute.For<IStokvelContributionRepository>();
            ledger.ListForMemberAsync(Alice.Id, Arg.Any<CancellationToken>())
                .Returns(contributions.Where(c => c.MemberId == Alice.Id).ToList());
            ledger.ListForMemberAsync(Bob.Id, Arg.Any<CancellationToken>())
                .Returns(contributions.Where(c => c.MemberId == Bob.Id).ToList());
            ledger.ListForGroupAsync(Group.Id, Arg.Any<CancellationToken>())
                .Returns(contributions);
            ledger.ListBenefitsForMemberAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(new List<StokvelBenefitAllocation>());
            ledger.ListBenefitsForGroupAsync(Group.Id, Arg.Any<CancellationToken>())
                .Returns(new List<StokvelBenefitAllocation>());

            var payouts = Substitute.For<IStokvelPayoutRepository>();
            payouts.ListForMemberAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(new List<StokvelPayout>());
            payouts.ListForGroupAsync(Group.Id, Arg.Any<CancellationToken>())
                .Returns(new List<StokvelPayout>());

            Members = new GetMemberStatementQueryHandler(groups, ledger, payouts);
            Groups = new GetGroupStatementQueryHandler(groups, ledger, payouts);
        }

        public static Wall Private() => new("Members see their own line only.");
        public static Wall Open() => new("Payouts in December. members-see-all.");
    }
}
