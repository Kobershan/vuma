using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.CustomerAccounts.Commands.Stokvels;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Pos;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.UnitTests.CustomerAccounts;

/// <summary>
/// Handler refusal paths: the approval gate is called on every payout, and a gate with no call
/// sites is the defect this test exists to prevent.
/// </summary>
public sealed class StokvelCommandTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid CompanyId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Payout_needs_approval()
    {
        var payout = StokvelPayout.Request(
            TenantId, null, UuidV7.NewGuid(), UuidV7.NewGuid(),
            StokvelPayoutKind.Cash, new Money(100m, "ZAR"), null, Now);

        var payouts = Substitute.For<IStokvelPayoutRepository>();
        payouts.FindAsync(payout.Id, Arg.Any<CancellationToken>()).Returns(payout);

        var approvals = Substitute.For<IApprovalService>();
        approvals.EvaluateAsync(Arg.Any<ApprovalContext>(), Arg.Any<CancellationToken>())
            .Returns(new ApprovalOutcome(ApprovalOutcomeKind.Pending, UuidV7.NewGuid()));

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var handler = new ApprovePayoutCommandHandler(payouts, approvals, clock);

        Func<Task> approving = () => handler.HandleAsync(new ApprovePayoutCommand(payout.Id));

        await approving.Should().ThrowAsync<StokvelExceptions>()
            .WithMessage("*approval*");
        await approvals.Received(1).EvaluateAsync(
            Arg.Is<ApprovalContext>(c =>
                c.Module == "customer-accounts" && c.SubjectEntityId == payout.Id),
            Arg.Any<CancellationToken>());
        payout.Status.Should().Be(StokvelPayoutStatus.Requested);
    }

    [Fact]
    public async Task Contribution_replay_returns_the_first_row()
    {
        var group = StokvelGroup.Create(
            TenantId, null, "STK-000002", "Grocery", StokvelType.Savings,
            "constitution", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15),
            UuidV7.NewGuid(), CompanyId);
        var member = StokvelMember.Join(
            TenantId, null, group.Id, UuidV7.NewGuid(),
            MemberRole.Member, new Money(500m, "ZAR"), Now);
        var first = StokvelContribution.Record(
            TenantId, null, group.Id, member.Id, new Money(100m, "ZAR"),
            "RCPT-DUP", Now, "Till");

        var groups = Substitute.For<IStokvelGroupRepository>();
        groups.FindAsync(group.Id, Arg.Any<CancellationToken>()).Returns(group);
        groups.FindMemberAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        var ledger = Substitute.For<IStokvelContributionRepository>();
        ledger.FindByReceiptAsync(member.Id, "RCPT-DUP", Arg.Any<CancellationToken>())
            .Returns(first);
        var events = Substitute.For<IFinancialEventPoster>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns((Guid?)CompanyId);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var handler = new RecordContributionCommandHandler(
            groups, ledger, events, tenant, clock);

        Guid id = await handler.HandleAsync(new RecordContributionCommand(
            group.Id, member.Id, 100m, "ZAR", "Till", "RCPT-DUP"));

        id.Should().Be(first.Id);
        await events.DidNotReceive().PostAsync(
            Arg.Any<IFinancialEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Joining_a_paying_out_group_or_twice_refuses()
    {
        var group = StokvelGroup.Create(
            TenantId, null, "STK-000003", "Grocery", StokvelType.Savings,
            "constitution", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15),
            UuidV7.NewGuid());
        group.Activate();
        group.BeginPayout();
        var existing = StokvelMember.Join(
            TenantId, null, group.Id, UuidV7.NewGuid(),
            MemberRole.Member, new Money(500m, "ZAR"), Now);

        var groups = Substitute.For<IStokvelGroupRepository>();
        groups.FindAsync(group.Id, Arg.Any<CancellationToken>()).Returns(group);
        groups.ListMembersAsync(group.Id, Arg.Any<CancellationToken>())
            .Returns(new List<StokvelMember> { existing });
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var handler = new AddStokvelMemberCommandHandler(groups, tenant, clock);

        Func<Task> closed = () => handler.HandleAsync(new AddStokvelMemberCommand(
            group.Id, UuidV7.NewGuid(), MemberRole.Member, 500m, "ZAR"));
        await closed.Should().ThrowAsync<StokvelExceptions>();
    }

    [Fact]
    public async Task Duplicate_member_refuses_while_active()
    {
        var group = StokvelGroup.Create(
            TenantId, null, "STK-000004", "Grocery", StokvelType.Savings,
            "constitution", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15),
            UuidV7.NewGuid());
        Guid partnerId = UuidV7.NewGuid();
        var existing = StokvelMember.Join(
            TenantId, null, group.Id, partnerId,
            MemberRole.Member, new Money(500m, "ZAR"), Now);

        var groups = Substitute.For<IStokvelGroupRepository>();
        groups.FindAsync(group.Id, Arg.Any<CancellationToken>()).Returns(group);
        groups.ListMembersAsync(group.Id, Arg.Any<CancellationToken>())
            .Returns(new List<StokvelMember> { existing });
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var handler = new AddStokvelMemberCommandHandler(groups, tenant, clock);

        Func<Task> twice = () => handler.HandleAsync(new AddStokvelMemberCommand(
            group.Id, partnerId, MemberRole.Member, 500m, "ZAR"));
        await twice.Should().ThrowAsync<StokvelExceptions>()
            .WithMessage("*already an active member*");
    }

    [Fact]
    public async Task Contribution_to_a_left_member_or_foreign_group_refuses()
    {
        var group = StokvelGroup.Create(
            TenantId, null, "STK-000005", "Grocery", StokvelType.Savings,
            "constitution", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15),
            UuidV7.NewGuid());
        var left = StokvelMember.Join(
            TenantId, null, group.Id, UuidV7.NewGuid(),
            MemberRole.Member, new Money(500m, "ZAR"), Now);
        left.Leave(Now);
        var stranger = StokvelMember.Join(
            TenantId, null, UuidV7.NewGuid(), UuidV7.NewGuid(),
            MemberRole.Member, new Money(500m, "ZAR"), Now);

        var groups = Substitute.For<IStokvelGroupRepository>();
        groups.FindAsync(group.Id, Arg.Any<CancellationToken>()).Returns(group);
        groups.FindMemberAsync(left.Id, Arg.Any<CancellationToken>()).Returns(left);
        groups.FindMemberAsync(stranger.Id, Arg.Any<CancellationToken>()).Returns(stranger);
        var ledger = Substitute.For<IStokvelContributionRepository>();
        var events = Substitute.For<IFinancialEventPoster>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var handler = new RecordContributionCommandHandler(groups, ledger, events, tenant, clock);

        Func<Task> afterLeaving = () => handler.HandleAsync(new RecordContributionCommand(
            group.Id, left.Id, 100m, "ZAR", "Till", "RCPT-L"));
        await afterLeaving.Should().ThrowAsync<StokvelExceptions>();

        Func<Task> foreign = () => handler.HandleAsync(new RecordContributionCommand(
            group.Id, stranger.Id, 100m, "ZAR", "Till", "RCPT-F"));
        await foreign.Should().ThrowAsync<StokvelExceptions>();
    }

    [Fact]
    public async Task Cash_settle_posts_the_payout_event_without_a_sale()
    {
        var group = StokvelGroup.Create(
            TenantId, null, "STK-000006", "Savings", StokvelType.Savings,
            "constitution", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15),
            UuidV7.NewGuid(), CompanyId);
        var member = StokvelMember.Join(
            TenantId, null, group.Id, UuidV7.NewGuid(),
            MemberRole.Member, new Money(500m, "ZAR"), Now);
        var payout = StokvelPayout.Request(
            TenantId, null, group.Id, member.Id, StokvelPayoutKind.Cash,
            new Money(100m, "ZAR"), null, Now);
        payout.Approve(Now);

        var groups = Substitute.For<IStokvelGroupRepository>();
        groups.FindAsync(group.Id, Arg.Any<CancellationToken>()).Returns(group);
        groups.FindMemberAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        var contribution = StokvelContribution.Record(
            TenantId, null, group.Id, member.Id, new Money(500m, "ZAR"),
            "RCPT-1", Now, "Till");
        var ledger = Substitute.For<IStokvelContributionRepository>();
        ledger.ListForMemberAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(new List<StokvelContribution> { contribution });
        ledger.ListBenefitsForMemberAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(new List<StokvelBenefitAllocation>());
        var payouts = Substitute.For<IStokvelPayoutRepository>();
        payouts.FindAsync(payout.Id, Arg.Any<CancellationToken>()).Returns(payout);
        payouts.ListForMemberAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(new List<StokvelPayout>());
        var terms = Substitute.For<ICustomerFinanceTermsRepository>();
        terms.FindAsync(Arg.Any<CancellationToken>())
            .Returns(CustomerFinanceTerms.Seed(TenantId, "ZAR"));
        var events = Substitute.For<IFinancialEventPoster>();
        var posted = new List<IFinancialEvent>();
        events.PostAsync(Arg.Do<IFinancialEvent>(posted.Add), Arg.Any<CancellationToken>())
            .Returns(UuidV7.NewGuid());
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns((Guid?)CompanyId);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var completion = Substitute.For<ISaleCompletionService>();

        var handler = new SettlePayoutCommandHandler(
            groups, ledger, payouts, terms,
            Substitute.For<IStockReservationRepository>(),
            Substitute.For<IReservationService>(),
            Substitute.For<IAvailabilityService>(),
            Substitute.For<ISellableItemResolver>(),
            Substitute.For<ITaxCalculator>(),
            Substitute.For<ISaleRepository>(),
            Substitute.For<IDocumentNumberSequence>(),
            completion, events, tenant, company, clock);

        Guid id = await handler.HandleAsync(new SettlePayoutCommand(payout.Id));

        id.Should().Be(payout.Id);
        payout.Status.Should().Be(StokvelPayoutStatus.Settled);
        payout.SaleId.Should().BeNull();
        posted.Should().ContainSingle();
        posted[0].EventType.Should().Be("stokvel.payout.settled");
        await completion.DidNotReceive().CompleteAsync(
            Arg.Any<Sale>(), Arg.Any<CancellationToken>());
    }
}
