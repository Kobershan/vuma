using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.CustomerAccounts.Commands.Stokvels;
using VumaRetail.Application.CustomerAccounts.Queries;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Pos;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.CustomerAccounts;

/// <summary>
/// Stokvel payouts end to end at the handler level: a hamper payout builds exactly one sale,
/// issues the stock once, and moves both balances; refusals fire before any approval.
/// </summary>
public sealed class StokvelPayoutTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid CompanyId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Hamper_payout_issues_stock_and_reduces_both_balances()
    {
        StokvelHarness harness = StokvelHarness.WithBalance(paidIn: 500m, paidAt: Now);

        Guid payoutId = await harness.RequestAsync(StokvelPayoutKind.Hamper, 450m);
        await harness.ApproveAsync(payoutId);
        await harness.SettleAsync(payoutId);

        // Exactly one sale, through the completion service exactly once.
        await harness.Completion.Received(1).CompleteAsync(
            Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        harness.Sales.Should().ContainSingle();
        Sale sale = harness.Sales.Single();
        sale.Gross.Amount.Should().Be(450m);
        sale.Lines.Should().ContainSingle();
        sale.Tenders.Should().ContainSingle();
        sale.Tenders.Single().Type.Should().Be(TenderType.Voucher);

        // Tax split the inclusive R450 the way the shelf price works: net + tax = gross.
        sale.Net.Amount.Should().BeApproximately(391.3043m, 0.001m);
        sale.Tax.Amount.Should().BeApproximately(58.6957m, 0.001m);

        // One consume per chain: the single stock-issue set.
        await harness.Reservations.Received(1).ConsumeAsync(
            Arg.Any<Guid>(), sale.Id, Arg.Any<CancellationToken>());

        // Both balances moved: member R500 → R50, group R500 → R50.
        StokvelPayout payout = harness.Payouts.Single(p => p.Id == payoutId);
        payout.Status.Should().Be(StokvelPayoutStatus.Settled);
        payout.SaleId.Should().Be(sale.Id);

        MemberStatementResult statement = await harness.MemberStatementAsync();
        statement.Available.Amount.Should().Be(50m);
        GroupStatementResult group = await harness.GroupStatementAsync();
        group.GroupBalance.Amount.Should().Be(50m);
    }

    [Fact]
    public async Task Payout_beyond_available_refuses()
    {
        StokvelHarness harness = StokvelHarness.WithBalance(paidIn: 500m, paidAt: Now);

        Func<Task> requesting = () => harness.RequestAsync(StokvelPayoutKind.Cash, 501m);

        await requesting.Should().ThrowAsync<StokvelExceptions>()
            .WithMessage("*available*");
        harness.Payouts.Should().BeEmpty();
        await harness.Approvals.DidNotReceive().EvaluateAsync(
            Arg.Any<ApprovalContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stale_balance_payout_needs_connectivity()
    {
        // Last activity an hour ago against a 15-minute freshness threshold.
        StokvelHarness harness = StokvelHarness.WithBalance(paidIn: 500m, paidAt: Now.AddHours(-1));

        Func<Task> offline = () => harness.RequestAsync(StokvelPayoutKind.Cash, 100m, capturedOffline: true);

        await offline.Should().ThrowAsync<StokvelExceptions>()
            .WithMessage("*connectivity*");

        // The same payout online is fine: contributions never refuse offline, stale reads do.
        Guid payoutId = await harness.RequestAsync(StokvelPayoutKind.Cash, 100m, capturedOffline: false);
        payoutId.Should().NotBe(Guid.Empty);
    }

    /// <summary>A whole stokvel in memory: one group, one member, one basket, faked ports.</summary>
    private sealed class StokvelHarness
    {
        private readonly StokvelGroup _group;
        private readonly StokvelMember _member;
        private readonly List<StokvelContribution> _contributions = [];
        private readonly List<StokvelPayout> _payouts = [];
        private readonly List<Sale> _sales = [];
        private readonly IClock _clock;

        public IReadOnlyList<StokvelPayout> Payouts => _payouts;
        public IReadOnlyList<Sale> Sales => _sales;
        public IReservationService Reservations { get; }
        public ISaleCompletionService Completion { get; }
        public IApprovalService Approvals { get; }

        private readonly RequestPayoutCommandHandler _request;
        private readonly ApprovePayoutCommandHandler _approve;
        private readonly SettlePayoutCommandHandler _settle;
        private readonly GetMemberStatementQueryHandler _memberStatement;
        private readonly GetGroupStatementQueryHandler _groupStatement;

        private StokvelHarness(decimal paidIn, DateTimeOffset paidAt, IClock clock)
        {
            _clock = clock;
            var tenant = Substitute.For<ITenantContext>();
            tenant.TenantId.Returns(TenantId);
            tenant.StoreId.Returns(StoreId);
            var company = Substitute.For<ICompanyContext>();
            company.CompanyId.Returns((Guid?)CompanyId);
            company.RequireCompany().Returns(CompanyId);

            _group = StokvelGroup.Create(
                TenantId, StoreId, "STK-000001", "Grocery", StokvelType.GroceryHamper,
                "Members see their own line only.", new DateOnly(2026, 1, 1),
                new DateOnly(2026, 12, 15), StoreId, CompanyId);
            _member = StokvelMember.Join(
                TenantId, StoreId, _group.Id, UuidV7.NewGuid(),
                MemberRole.Member, new Money(500m, "ZAR"), paidAt);
            var basket = HamperBasket.Create(
                TenantId, StoreId, _group.Id, "December hamper",
                new Money(450m, "ZAR"), new DateOnly(2026, 12, 1),
                new DateOnly(2026, 12, 24), LocationId, CompanyId);
            basket.AddLine(HamperBasketLine.Create(
                TenantId, StoreId, basket.Id, ItemId, null, 2m, "EA"));

            var groups = Substitute.For<IStokvelGroupRepository>();
            groups.FindAsync(_group.Id, Arg.Any<CancellationToken>()).Returns(_group);
            groups.FindAsync(Arg.Is<Guid>(id => id != _group.Id), Arg.Any<CancellationToken>())
                .Returns((StokvelGroup?)null);
            groups.FindMemberAsync(_member.Id, Arg.Any<CancellationToken>()).Returns(_member);
            groups.FindBasketAsync(basket.Id, Arg.Any<CancellationToken>()).Returns(basket);
            groups.ListMembersAsync(_group.Id, Arg.Any<CancellationToken>())
                .Returns(new List<StokvelMember> { _member });

            _contributions.Add(StokvelContribution.Record(
                TenantId, StoreId, _group.Id, _member.Id, new Money(paidIn, "ZAR"),
                "RCPT-1", paidAt, "Till"));

            var ledger = Substitute.For<IStokvelContributionRepository>();
            ledger.ListForMemberAsync(_member.Id, Arg.Any<CancellationToken>())
                .Returns(call => _contributions.Where(c => c.MemberId == _member.Id).ToList());
            ledger.ListForGroupAsync(_group.Id, Arg.Any<CancellationToken>())
                .Returns(call => _contributions.Where(c => c.GroupId == _group.Id).ToList());
            ledger.ListBenefitsForMemberAsync(_member.Id, Arg.Any<CancellationToken>())
                .Returns(new List<StokvelBenefitAllocation>());
            ledger.ListBenefitsForGroupAsync(_group.Id, Arg.Any<CancellationToken>())
                .Returns(new List<StokvelBenefitAllocation>());
            ledger.FindByReceiptAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => _contributions.FirstOrDefault(
                    c => c.MemberId == call.Arg<Guid>() && c.ReceiptReference == call.Arg<string>()));
            ledger.AddContribution(Arg.Do<StokvelContribution>(_contributions.Add));
            ledger.AddBenefit(Arg.Do<StokvelBenefitAllocation>(_ => { }));

            var payoutRepo = Substitute.For<IStokvelPayoutRepository>();
            payoutRepo.Add(Arg.Do<StokvelPayout>(_payouts.Add));
            payoutRepo.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => _payouts.FirstOrDefault(p => p.Id == call.Arg<Guid>()));
            payoutRepo.ListForMemberAsync(_member.Id, Arg.Any<CancellationToken>())
                .Returns(call => _payouts.Where(p => p.MemberId == _member.Id).ToList());
            payoutRepo.ListForGroupAsync(_group.Id, Arg.Any<CancellationToken>())
                .Returns(call => _payouts.Where(p => p.GroupId == _group.Id).ToList());

            var terms = Substitute.For<ICustomerFinanceTermsRepository>();
            terms.FindAsync(Arg.Any<CancellationToken>())
                .Returns(CustomerFinanceTerms.Seed(TenantId, "ZAR"));

            Reservations = Substitute.For<IReservationService>();
            Reservations.ReserveAsync(
                    Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<Quantity>(),
                    Arg.Any<ReservationSource>(), Arg.Any<Guid>(), Arg.Any<string?>(),
                    Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                    Arg.Any<CancellationToken>())
                .Returns(new ReserveOutcome(
                    UuidV7.NewGuid(), new Quantity(2m, "EA"), new Quantity(0m, "EA"),
                    new Quantity(0m, "EA"), Now));

            var holds = Substitute.For<IStockReservationRepository>();
            holds.ListOpenByGroupRefAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => _payouts
                    .Select(p => StockReservation.Hold(
                        TenantId, StoreId, CompanyId, LocationId, ItemId, null,
                        new Quantity(2m, "EA"), ReservationSource.StokvelHamper, p.Id,
                        _group.GroupNumber))
                    .ToList());

            var availability = Substitute.For<IAvailabilityService>();
            availability.GetLocalAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(new LocalAvailability(
                    LocationId, ItemId, null,
                    new AvailableToPromise(
                        new Quantity(10m, "EA"), new Quantity(0m, "EA"),
                        new Quantity(0m, "EA"), new Quantity(0m, "EA"), Now)));

            var catalog = Substitute.For<ISellableItemResolver>();
            catalog.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(new SellableItem(ItemId, null, "Milk 2L", "EA", "STANDARD"));

            var tax = Substitute.For<ITaxCalculator>();
            tax.CalculateAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var gross = call.Arg<Money>();
                    var net = new Money(gross.Amount * 100m / 115m, gross.Currency);
                    return new TaxCalculation("STANDARD", net, gross - net, gross, 0.15m);
                });

            var sales = Substitute.For<ISaleRepository>();
            sales.Add(Arg.Do<Sale>(_sales.Add));

            var numbers = Substitute.For<IDocumentNumberSequence>();
            numbers.NextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => $"{call.Arg<string>()}-000001");

            Completion = Substitute.For<ISaleCompletionService>();
            var events = Substitute.For<IFinancialEventPoster>();
            events.PostAsync(Arg.Any<IFinancialEvent>(), Arg.Any<CancellationToken>())
                .Returns(UuidV7.NewGuid());
            Approvals = Substitute.For<IApprovalService>();
            Approvals.EvaluateAsync(Arg.Any<ApprovalContext>(), Arg.Any<CancellationToken>())
                .Returns(VumaRetail.Application.Abstractions.Workflow.ApprovalOutcome.NoGate);

            _request = new RequestPayoutCommandHandler(
                groups, ledger, payoutRepo, terms, Reservations, tenant, company, _clock);
            _approve = new ApprovePayoutCommandHandler(
                payoutRepo, Approvals, _clock);
            _settle = new SettlePayoutCommandHandler(
                groups, ledger, payoutRepo, terms, holds, Reservations, availability,
                catalog, tax, sales, numbers, Completion, events, tenant, company, _clock);
            _memberStatement = new GetMemberStatementQueryHandler(groups, ledger, payoutRepo);
            _groupStatement = new GetGroupStatementQueryHandler(groups, ledger, payoutRepo);
            BasketId = basket.Id;
            Member = _member;
            Group = _group;
        }

        public Guid BasketId { get; }
        public StokvelMember Member { get; }
        public StokvelGroup Group { get; }

        public static StokvelHarness WithBalance(decimal paidIn, DateTimeOffset paidAt)
        {
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(Now);
            return new StokvelHarness(paidIn, paidAt, clock);
        }

        public Task<Guid> RequestAsync(StokvelPayoutKind kind, decimal amount, bool capturedOffline = false)
            => _request.HandleAsync(new RequestPayoutCommand(
                Group.Id, Member.Id, kind, amount, "ZAR",
                kind is StokvelPayoutKind.Goods or StokvelPayoutKind.Hamper ? BasketId : null,
                capturedOffline));

        public Task ApproveAsync(Guid payoutId)
            => _approve.HandleAsync(new ApprovePayoutCommand(payoutId));

        public Task<Guid> SettleAsync(Guid payoutId)
            => _settle.HandleAsync(new SettlePayoutCommand(payoutId));

        public Task<MemberStatementResult> MemberStatementAsync()
            => _memberStatement.HandleAsync(
                new GetMemberStatementQuery(Group.Id, Member.Id, Member.PartnerId, MemberRole.Member));

        public Task<GroupStatementResult> GroupStatementAsync()
            => _groupStatement.HandleAsync(new GetGroupStatementQuery(Group.Id, MemberRole.Treasurer));
    }
}
