using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.CustomerAccounts.Commands.Stokvels;
using VumaRetail.Application.CustomerAccounts.Hosting;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.UnitTests.CustomerAccounts;

/// <summary>
/// The stokvel happy paths at the handler level: groups open, members join, contributions post
/// to liability, benefits split time-weighted with an audit basis, leaving refunds pro-rata,
/// and the reminder pass nudges arrears and the December rush.
/// </summary>
public sealed class StokvelHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid CompanyId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Group_opens_members_join_and_contributions_post_to_liability()
    {
        var groups = Substitute.For<IStokvelGroupRepository>();
        var ledger = Substitute.For<IStokvelContributionRepository>();
        var numbers = Substitute.For<IDocumentNumberSequence>();
        numbers.NextAsync("STK", Arg.Any<CancellationToken>()).Returns("STK-000001");
        var tenant = Tenant();
        var company = Company();
        var clock = Clock();
        var events = Substitute.For<IFinancialEventPoster>();
        var posted = new List<IFinancialEvent>();
        events.PostAsync(Arg.Do<IFinancialEvent>(posted.Add), Arg.Any<CancellationToken>())
            .Returns(UuidV7.NewGuid());

        StokvelGroup? created = null;
        groups.AddGroup(Arg.Do<StokvelGroup>(g => created = g));
        var create = new CreateStokvelGroupCommandHandler(groups, numbers, tenant, company);
        Guid groupId = await create.HandleAsync(new CreateStokvelGroupCommand(
            "Grocery", StokvelType.GroceryHamper, "constitution",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15), StoreId, CompanyId));

        created.Should().NotBeNull();
        created!.GroupNumber.Should().Be("STK-000001");
        created.CompanyId.Should().Be(CompanyId);
        created.Status.Should().Be(StokvelStatus.Forming);

        groups.FindAsync(groupId, Arg.Any<CancellationToken>()).Returns(created);
        StokvelMember? joined = null;
        groups.AddMember(Arg.Do<StokvelMember>(m => joined = m));
        groups.ListMembersAsync(groupId, Arg.Any<CancellationToken>())
            .Returns(new List<StokvelMember>());
        var join = new AddStokvelMemberCommandHandler(groups, tenant, clock);
        Guid memberId = await join.HandleAsync(new AddStokvelMemberCommand(
            groupId, UuidV7.NewGuid(), MemberRole.Treasurer, 500m, "ZAR"));

        joined.Should().NotBeNull();
        joined!.Role.Should().Be(MemberRole.Treasurer);
        memberId.Should().Be(joined.Id);

        groups.FindMemberAsync(memberId, Arg.Any<CancellationToken>()).Returns(joined);
        ledger.FindByReceiptAsync(memberId, "RCPT-1", Arg.Any<CancellationToken>())
            .Returns((StokvelContribution?)null);
        StokvelContribution? row = null;
        ledger.AddContribution(Arg.Do<StokvelContribution>(c => row = c));
        var record = new RecordContributionCommandHandler(groups, ledger, events, tenant, clock);
        Guid contributionId = await record.HandleAsync(new RecordContributionCommand(
            groupId, memberId, 500m, "ZAR", "Till", "RCPT-1"));

        row.Should().NotBeNull();
        contributionId.Should().Be(row!.Id);
        posted.Should().ContainSingle();
        posted[0].EventType.Should().Be("stokvel.contribution.received");
        posted[0].Amounts["Principal"].Amount.Should().Be(500m);

        created.Activate();
        created.Status.Should().Be(StokvelStatus.Active);
        created.BeginPayout();
        created.Status.Should().Be(StokvelStatus.PayingOut);
        created.Close();
        created.Status.Should().Be(StokvelStatus.Closed);
    }

    [Fact]
    public async Task AllocateBenefits_writes_time_weighted_shares_with_the_weight_basis()
    {
        DateTimeOffset early = Now.AddDays(-100);
        var alice = StokvelMember.Join(
            TenantId, StoreId, UuidV7.NewGuid(), UuidV7.NewGuid(),
            MemberRole.Member, new Money(500m, "ZAR"), early);
        var bob = StokvelMember.Join(
            TenantId, StoreId, alice.GroupId, UuidV7.NewGuid(),
            MemberRole.Member, new Money(500m, "ZAR"), Now.AddDays(-50));
        var group = StokvelGroup.Create(
            TenantId, StoreId, "STK-9", "Grocery", StokvelType.Savings,
            "constitution", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15),
            StoreId, CompanyId);

        var contributions = new List<StokvelContribution>
        {
            StokvelContribution.Record(
                TenantId, StoreId, group.Id, alice.Id, new Money(3000m, "ZAR"),
                "RCPT-A", early, "Till"),
            StokvelContribution.Record(
                TenantId, StoreId, group.Id, bob.Id, new Money(3000m, "ZAR"),
                "RCPT-B", bob.JoinedAt, "Till"),
        };

        var groups = Substitute.For<IStokvelGroupRepository>();
        groups.FindAsync(group.Id, Arg.Any<CancellationToken>()).Returns(group);
        groups.ListMembersAsync(group.Id, Arg.Any<CancellationToken>())
            .Returns(new List<StokvelMember> { alice, bob });
        var ledger = Substitute.For<IStokvelContributionRepository>();
        ledger.ListForGroupAsync(group.Id, Arg.Any<CancellationToken>()).Returns(contributions);
        var added = new List<StokvelBenefitAllocation>();
        ledger.AddBenefit(Arg.Do<StokvelBenefitAllocation>(added.Add));
        var events = Substitute.For<IFinancialEventPoster>();
        events.PostAsync(Arg.Any<IFinancialEvent>(), Arg.Any<CancellationToken>())
            .Returns(UuidV7.NewGuid());

        var handler = new AllocateBenefitsCommandHandler(
            groups, ledger, events, Tenant(), Clock());
        IReadOnlyList<Guid> ids = await handler.HandleAsync(
            new AllocateBenefitsCommand(group.Id, 300m, "ZAR", Now));

        // Alice held 100 days, Bob 50: weights 300,000 vs 150,000 → R200 / R100.
        ids.Should().HaveCount(2);
        added.Should().HaveCount(2);
        added.First(b => b.MemberId == alice.Id).Amount.Amount.Should().Be(200m);
        added.First(b => b.MemberId == bob.Id).Amount.Amount.Should().Be(100m);
        added.Should().OnlyContain(b => b.Basis.Contains("time-weighted"));
        await events.Received(2).PostAsync(
            Arg.Is<IFinancialEvent>(e => e.EventType == "stokvel.benefit.allocated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveMember_refunds_pro_rata_and_keeps_the_rows()
    {
        var group = StokvelGroup.Create(
            TenantId, StoreId, "STK-7", "Grocery", StokvelType.Savings,
            "constitution", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15),
            StoreId, CompanyId);
        var alice = StokvelMember.Join(
            TenantId, StoreId, group.Id, UuidV7.NewGuid(),
            MemberRole.Member, new Money(500m, "ZAR"), Now);
        var bob = StokvelMember.Join(
            TenantId, StoreId, group.Id, UuidV7.NewGuid(),
            MemberRole.Member, new Money(500m, "ZAR"), Now);

        // Paid R10,000 (A 6,000 / B 4,000), R1,000 committed, R100 fee: A gets R5,300.
        var contributions = new List<StokvelContribution>
        {
            StokvelContribution.Record(
                TenantId, StoreId, group.Id, alice.Id, new Money(6000m, "ZAR"),
                "RCPT-A", Now, "Till"),
            StokvelContribution.Record(
                TenantId, StoreId, group.Id, bob.Id, new Money(4000m, "ZAR"),
                "RCPT-B", Now, "Till"),
        };
        var settled = StokvelPayout.Request(
            TenantId, StoreId, group.Id, bob.Id, StokvelPayoutKind.Cash,
            new Money(1000m, "ZAR"), null, Now);
        settled.Approve(Now);
        settled.Settle(Now);

        var groups = Substitute.For<IStokvelGroupRepository>();
        groups.FindAsync(group.Id, Arg.Any<CancellationToken>()).Returns(group);
        groups.FindMemberAsync(alice.Id, Arg.Any<CancellationToken>()).Returns(alice);
        var ledger = Substitute.For<IStokvelContributionRepository>();
        ledger.ListForGroupAsync(group.Id, Arg.Any<CancellationToken>()).Returns(contributions);
        ledger.ListBenefitsForGroupAsync(group.Id, Arg.Any<CancellationToken>())
            .Returns(new List<StokvelBenefitAllocation>());
        var payouts = Substitute.For<IStokvelPayoutRepository>();
        payouts.ListForGroupAsync(group.Id, Arg.Any<CancellationToken>())
            .Returns(new List<StokvelPayout> { settled });
        var terms = Substitute.For<ICustomerFinanceTermsRepository>();
        terms.FindAsync(Arg.Any<CancellationToken>())
            .Returns(CustomerFinanceTerms.Seed(TenantId, "ZAR"));

        var handler = new RemoveMemberCommandHandler(groups, ledger, payouts, terms, Clock());
        Money refund = await handler.HandleAsync(new RemoveMemberCommand(group.Id, alice.Id));

        refund.Amount.Should().Be(5300m);
        alice.LeftAt.Should().NotBeNull();
        (await groups.FindMemberAsync(alice.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Hamper_creation_freezes_lines_with_substitution()
    {
        var group = StokvelGroup.Create(
            TenantId, StoreId, "STK-8", "Grocery", StokvelType.GroceryHamper,
            "constitution", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15),
            StoreId, CompanyId);
        var groups = Substitute.For<IStokvelGroupRepository>();
        groups.FindAsync(group.Id, Arg.Any<CancellationToken>()).Returns(group);
        HamperBasket? basket = null;
        groups.AddBasket(Arg.Do<HamperBasket>(b => basket = b));
        var locations = Substitute.For<IStockLocationRepository>();
        locations.FindByCodeAsync("MAIN", Arg.Any<CancellationToken>())
            .Returns(StockLocation.Create(TenantId, StoreId, "MAIN", "Back room", StockLocationType.Warehouse));

        var handler = new CreateHamperBasketCommandHandler(groups, locations, Tenant(), Company());
        Guid basketId = await handler.HandleAsync(new CreateHamperBasketCommand(
            group.Id, "December hamper", 450m, "ZAR",
            new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 24), "MAIN",
            [new HamperLineInput(ItemId, null, 2m, "EA", UuidV7.NewGuid(), null)],
            CompanyId));

        basket.Should().NotBeNull();
        basketId.Should().Be(basket!.Id);
        basket.GroupPrice.Amount.Should().Be(450m);
        basket.Lines.Should().ContainSingle();
        basket.Lines[0].SubstitutionItemId.Should().NotBeNull();
        basket.IsInSeason(new DateOnly(2026, 12, 10)).Should().BeTrue();
        basket.IsInSeason(new DateOnly(2026, 11, 10)).Should().BeFalse();
    }

    [Fact]
    public async Task Reminder_pass_nudges_arrears_and_the_october_rush()
    {
        var group = StokvelGroup.Create(
            TenantId, StoreId, "STK-6", "Grocery", StokvelType.GroceryHamper,
            "constitution", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15),
            StoreId, CompanyId);
        group.Activate();
        var shortMember = StokvelMember.Join(
            TenantId, StoreId, group.Id, UuidV7.NewGuid(),
            MemberRole.Member, new Money(500m, "ZAR"), Now);
        var treasurer = StokvelMember.Join(
            TenantId, StoreId, group.Id, UuidV7.NewGuid(),
            MemberRole.Treasurer, new Money(500m, "ZAR"), Now);
        var basket = HamperBasket.Create(
            TenantId, StoreId, group.Id, "December hamper", new Money(450m, "ZAR"),
            new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 24), LocationId, CompanyId);

        var groups = Substitute.For<IStokvelGroupRepository>();
        groups.FindAsync(group.Id, Arg.Any<CancellationToken>()).Returns(group);
        groups.ListMembersAsync(group.Id, Arg.Any<CancellationToken>())
            .Returns(new List<StokvelMember> { shortMember, treasurer });
        groups.ListBasketsAsync(group.Id, Arg.Any<CancellationToken>())
            .Returns(new List<HamperBasket> { basket });
        var ledger = Substitute.For<IStokvelContributionRepository>();
        ledger.ListForGroupAsync(group.Id, Arg.Any<CancellationToken>())
            .Returns(new List<StokvelContribution>
            {
                StokvelContribution.Record(
                    TenantId, StoreId, group.Id, shortMember.Id, new Money(100m, "ZAR"),
                    "RCPT-1", Now, "Till"),
                StokvelContribution.Record(
                    TenantId, StoreId, group.Id, treasurer.Id, new Money(500m, "ZAR"),
                    "RCPT-2", Now, "Till"),
            });
        var dispatcher = Substitute.For<INotificationDispatcher>();
        var sent = new List<NotificationRequest>();
        dispatcher.NotifyAsync(Arg.Do<NotificationRequest>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { UuidV7.NewGuid() });

        int october = await StokvelReminderHostedService.RemindGroupAsync(
            groups, ledger, dispatcher, group.Id, new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));

        // One arrears nudge plus the October hamper-season check to the treasurer.
        october.Should().Be(2);
        sent.Should().Contain(r => r.Category == "customer-accounts.stokvel.arrears");
        sent.Should().Contain(r => r.Category == "customer-accounts.stokvel.hamper-season");

        sent.Clear();
        int november = await StokvelReminderHostedService.RemindGroupAsync(
            groups, ledger, dispatcher, group.Id, new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero));

        november.Should().Be(1);
    }

    private static ITenantContext Tenant()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        tenant.StoreId.Returns(StoreId);
        return tenant;
    }

    private static ICompanyContext Company()
    {
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns((Guid?)CompanyId);
        company.RequireCompany().Returns(CompanyId);
        return company;
    }

    private static IClock Clock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return clock;
    }
}
