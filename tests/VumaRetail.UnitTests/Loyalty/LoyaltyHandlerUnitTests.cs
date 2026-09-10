using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Loyalty;
using VumaRetail.Application.Loyalty.Commands;
using VumaRetail.Application.Loyalty.Hosting;
using VumaRetail.Application.Loyalty.Queries;
using VumaRetail.Domain.Loyalty;

namespace VumaRetail.UnitTests.Loyalty;

/// <summary>
/// Loyalty queries, member commands, sync paths and the hosted-service pass against stubbed
/// repositories: every not-found path, validators and the manifest (Stage 20).
/// </summary>
public sealed class LoyaltyHandlerUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();

    private sealed class Stubs
    {
        public ILoyaltyMemberRepository Members = Substitute.For<ILoyaltyMemberRepository>();
        public ILoyaltyTransactionRepository Transactions = Substitute.For<ILoyaltyTransactionRepository>();
        public ILoyaltyTierRepository Tiers = Substitute.For<ILoyaltyTierRepository>();
        public ILoyaltyRewardRepository Rewards = Substitute.For<ILoyaltyRewardRepository>();
        public ILoyaltySettingsRepository Settings = Substitute.For<ILoyaltySettingsRepository>();
        public IOrbitClient Orbit = Substitute.For<IOrbitClient>();
        public ITenantContext TenantContext = Substitute.For<ITenantContext>();
        public IClock Clock = Substitute.For<IClock>();
        public LoyaltyMember Member;
        public LoyaltySettings CompanySettings;

        public Stubs()
        {
            TenantContext.TenantId.Returns(Tenant);
            Clock.UtcNow.Returns(Now);
            Member = new LoyaltyMember(Tenant, Company, Customer, "orbit-1", Now);
            CompanySettings = new LoyaltySettings(Tenant, Company, "ZAR");
            CompanySettings.Enable(1m, 365);
            Members.FindByCustomerAsync(Customer, Arg.Any<CancellationToken>()).Returns(Member);
            Members.FindByCustomerAsync(Arg.Is<Guid>(id => id != Customer), Arg.Any<CancellationToken>())
                .Returns((LoyaltyMember?)null);
            Settings.FindAsync(Company, Arg.Any<CancellationToken>()).Returns(CompanySettings);
        }
    }

    [Fact]
    public async Task Get_member_reports_staleness_honestly()
    {
        var stubs = new Stubs();
        var handler = new GetMemberQueryHandler(stubs.Members, stubs.Clock);

        LoyaltyMemberEntry fresh = await handler.HandleAsync(new GetMemberQuery(Customer));
        fresh.IsStale.Should().BeFalse();

        stubs.Clock.UtcNow.Returns(Now.AddMinutes(6));
        LoyaltyMemberEntry stale = await handler.HandleAsync(new GetMemberQuery(Customer));
        stale.IsStale.Should().BeTrue();

        var missing = new GetMemberQueryHandler(
            Substitute.For<ILoyaltyMemberRepository>(), stubs.Clock);
        Func<Task> act = () => missing.HandleAsync(new GetMemberQuery(Guid.NewGuid()));
        await act.Should().ThrowAsync<LoyaltyMemberNotFoundException>();
    }

    [Fact]
    public async Task Balance_and_history_require_membership()
    {
        var stubs = new Stubs();
        var balance = new GetBalanceQueryHandler(stubs.Members, stubs.Clock);
        (await balance.HandleAsync(new GetBalanceQuery(Customer))).Balance.Should().Be(0m);

        var history = new ListTransactionsQueryHandler(stubs.Members, stubs.Transactions);
        stubs.Transactions.ListForMemberAsync(
                Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        (await history.HandleAsync(new ListTransactionsQuery(Customer, 10))).Should().BeEmpty();

        var ghost = new ListTransactionsQueryHandler(
            Substitute.For<ILoyaltyMemberRepository>(), stubs.Transactions);
        Func<Task> act = () => ghost.HandleAsync(new ListTransactionsQuery(Guid.NewGuid(), 10));
        await act.Should().ThrowAsync<LoyaltyMemberNotFoundException>();
    }

    [Fact]
    public async Task Tier_progress_and_rewards_list()
    {
        var stubs = new Stubs();
        stubs.Tiers.ListAsync(Company, Arg.Any<CancellationToken>()).Returns(
            (IReadOnlyList<LoyaltyTier>)
            [
                new LoyaltyTier(Tenant, Company, "bronze", "Bronze", "Bronze", 0m, 1m, Now),
                new LoyaltyTier(Tenant, Company, "silver", "Silver", "Silver", 1000m, 1.25m, Now),
            ]);

        var tiers = new ListTiersQueryHandler(stubs.Tiers);
        (await tiers.HandleAsync(new ListTiersQuery(Company))).Should().HaveCount(2);

        var progress = new GetMemberTierQueryHandler(stubs.Members, stubs.Tiers);
        MemberTierEntry entry = await progress.HandleAsync(new GetMemberTierQuery(Customer));
        entry.NextTierId.Should().Be("silver");
        entry.PointsToNext.Should().Be(1000m);

        stubs.Rewards.ListAsync(Company, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var rewards = new ListRewardsQueryHandler(stubs.Rewards);
        (await rewards.HandleAsync(new ListRewardsQuery(Company, null))).Should().BeEmpty();
    }

    [Fact]
    public async Task Replay_lookup_reports_terminal_only()
    {
        var stubs = new Stubs();
        var handler = new GetTransactionByKeyQueryHandler(stubs.Transactions);

        (await handler.HandleAsync(new GetTransactionByKeyQuery(Company, Guid.NewGuid())))
            .Should().BeNull();

        var confirmed = new LoyaltyTransaction(
            Tenant, Company, Customer, TransactionType.Earn, 10m, "ZAR",
            Guid.NewGuid(), null, Now);
        confirmed.MarkConfirmed("orbit-1", 10m);
        stubs.Transactions.FindByKeyAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(confirmed);

        TransactionReplayEntry? entry = await handler.HandleAsync(
            new GetTransactionByKeyQuery(Company, Guid.NewGuid()));
        entry.Should().NotBeNull();
        entry!.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public async Task Enroll_duplicate_and_disabled_paths()
    {
        var stubs = new Stubs();
        var enroll = new EnrollMemberCommandHandler(
            stubs.Members, stubs.Orbit, stubs.TenantContext, stubs.Clock);

        Func<Task> duplicate = () => enroll.HandleAsync(new EnrollMemberCommand(Company, Customer));
        await duplicate.Should().ThrowAsync<LoyaltyMemberAlreadyEnrolledException>();

        stubs.CompanySettings.Disable();
        var earn = new EarnPointsCommandHandler(
            stubs.Settings, stubs.Members, stubs.Transactions, stubs.Tiers,
            stubs.Orbit, Substitute.For<Application.Crm.IConsentService>(),
            stubs.TenantContext, stubs.Clock);
        Func<Task> disabled = () => earn.HandleAsync(new EarnPointsCommand(
            Company, Customer, 10m, "ZAR", null, Guid.NewGuid()));
        await disabled.Should().ThrowAsync<LoyaltyDisabledException>();
    }

    [Fact]
    public async Task Configure_creates_then_reconfigures()
    {
        var stubs = new Stubs();
        var configure = new ConfigureLoyaltyCommandHandler(stubs.Settings, stubs.TenantContext);

        await configure.HandleAsync(new ConfigureLoyaltyCommand(Company, "ZAR", 2m, 180, true));

        var created = new LoyaltySettings(Tenant, Company, "ZAR");
        stubs.Settings.FindAsync(Company, Arg.Any<CancellationToken>()).Returns(created);
        await configure.HandleAsync(new ConfigureLoyaltyCommand(Company, "ZAR", 2m, 180, true));
        created.EarnRate.Should().Be(2m);
        await configure.HandleAsync(new ConfigureLoyaltyCommand(Company, "ZAR", 2m, 180, false));
        created.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Webhook_unknown_member_is_ignored()
    {
        var stubs = new Stubs();
        var webhook = new ProcessLoyaltyWebhookCommandHandler(stubs.Members, stubs.Clock);

        await webhook.HandleAsync(new ProcessLoyaltyWebhookCommand(
            Company, "orbit-ghost", 10m, null, "balance.adjusted"));

        await stubs.Members.DidNotReceive().FindByCustomerAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retry_missing_transaction_is_not_found()
    {
        var stubs = new Stubs();
        var retry = new RetryLoyaltyTransactionCommandHandler(
            stubs.Transactions, stubs.Members, stubs.Orbit, stubs.Clock);

        Func<Task> act = () => retry.HandleAsync(
            new RetryLoyaltyTransactionCommand(Guid.NewGuid()));

        await act.Should().ThrowAsync<LoyaltyTransactionNotFoundException>();
    }

    [Fact]
    public async Task Reconciliation_reports_differences_without_correcting()
    {
        var stubs = new Stubs();
        var confirmed = new LoyaltyTransaction(
            Tenant, Company, Customer, TransactionType.Earn, 100m, "ZAR",
            Guid.NewGuid(), null, Now);
        confirmed.MarkConfirmed("orbit-1", 100m);
        stubs.Transactions.ListConfirmedAsync(Customer, Arg.Any<CancellationToken>())
            .Returns([confirmed]);
        stubs.Orbit.GetBalanceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new OrbitBalanceResult(90m, null));

        var reconcile = new GetReconciliationReportQueryHandler(
            stubs.Members, stubs.Transactions, stubs.Orbit);

        IReadOnlyList<ReconciliationLine> report = await reconcile.HandleAsync(
            new GetReconciliationReportQuery(Company, [Customer, Guid.NewGuid()], 10));

        report.Should().ContainSingle()
            .Which.Difference.Should().Be(10m);
    }

    [Fact]
    public async Task Retry_worker_confirms_queued_rows()
    {
        var stubs = new Stubs();
        var queued = new LoyaltyTransaction(
            Tenant, Company, Customer, TransactionType.Earn, 50m, "ZAR",
            Guid.NewGuid(), null, Now);
        queued.MarkQueuedForRetry();

        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.QueryAsync(Arg.Any<ListQueuedTransactionsQuery>(), Arg.Any<CancellationToken>())
            .Returns([queued.Id]);
        dispatcher.SendAsync(Arg.Any<RetryLoyaltyTransactionCommand>(), Arg.Any<CancellationToken>())
            .Returns(RetryDisposition.Confirmed);

        var services = new ServiceCollection();
        services.AddSingleton(dispatcher);
        services.AddSingleton<ITenantContext>(stubs.TenantContext);
        services.AddSingleton<LoyaltyHostTenant>(new LoyaltyHostTenant(Tenant, null));
        services.AddLogging();
        using ServiceProvider provider = services.BuildServiceProvider();

        var worker = new LoyaltyRetryHostedService(
            provider,
            new LoyaltyHostTenant(Tenant, null),
            NullLogger<LoyaltyRetryHostedService>.Instance);

        // The real loop, not a seam: start it, wait for the pass to dispatch, stop it.
        using var scope = new CancellationTokenSource();
        await worker.StartAsync(scope.Token);

        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (dispatcher.ReceivedCalls().Any())
            {
                break;
            }

            await Task.Delay(50, scope.Token);
        }

        await worker.StopAsync(CancellationToken.None);

        await dispatcher.Received(1).SendAsync(
            Arg.Is<RetryLoyaltyTransactionCommand>(command => command.TransactionId == queued.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Manifest_and_validators()
    {
        var manifest = new LoyaltyModuleManifest();
        manifest.Module.Should().Be("loyalty");
        manifest.LicenceFlag.Should().Be("loyalty");
        manifest.IsCore.Should().BeFalse();

        new EarnPointsCommandValidator()
            .Validate(new EarnPointsCommand(
                Guid.Empty, Guid.Empty, 0m, "ZZ", null, Guid.Empty))
            .IsValid.Should().BeFalse();
        new RedeemPointsCommandValidator()
            .Validate(new RedeemPointsCommand(Guid.Empty, Guid.Empty, -1m, null, Guid.Empty))
            .IsValid.Should().BeFalse();
        new EnrollMemberCommandValidator()
            .Validate(new EnrollMemberCommand(Guid.Empty, Guid.Empty))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Reward_guards_and_refresh()
    {
        Action bad = () => new LoyaltyReward(
            Tenant, Company, "r", "R", 0m);
        bad.Should().Throw<InvalidPointsException>();

        var reward = new LoyaltyReward(Tenant, Company, "r", "R", 100m);
        reward.RefreshAvailability(false, Now);
        reward.IsAvailable.Should().BeFalse();
        reward.SyncedAt.Should().Be(Now);
    }
}
