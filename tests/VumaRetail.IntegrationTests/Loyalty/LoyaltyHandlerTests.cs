using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Loyalty;
using VumaRetail.Application.Loyalty.Commands;
using VumaRetail.Application.Loyalty.Queries;
using VumaRetail.Domain.Loyalty;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Loyalty;

/// <summary>
/// The <c>loyalty</c> module against real PostgreSQL: earn/burn through the real in-memory Orbit
/// ledger, idempotent replay, the concurrent-burn gate, outage → queue → retry, and the
/// constraints that hold when the aggregate is bypassed (Stage 20).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LoyaltyHandlerTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Full_earn_flow_confirms_and_caches()
    {
        await using LoyaltyHarness harness = await LoyaltyHarness.CreateAsync(fixture);
        await harness.EnrollAsync();

        EarnOutcome outcome = await harness.Dispatcher.SendAsync(new EarnPointsCommand(
            harness.CompanyId, harness.CustomerId, 150m, "ZAR", "sale-1", Guid.NewGuid()));

        outcome.Queued.Should().BeFalse();
        outcome.Points.Should().Be(150m);
        outcome.NewBalance.Should().Be(150m);

        BalanceEntry balance = await harness.Dispatcher.QueryAsync(
            new GetBalanceQuery(harness.CustomerId));
        balance.Balance.Should().Be(150m);
        balance.IsStale.Should().BeFalse();

        LoyaltyTransaction? stored = await harness.Transactions.FindAsync(outcome.TransactionId);
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(TransactionStatus.Confirmed);
        stored.OrbitTransactionId.Should().NotBeNull();
    }

    [Fact]
    public async Task Full_redeem_flow_debits_and_caches()
    {
        await using LoyaltyHarness harness = await LoyaltyHarness.CreateAsync(fixture);
        await harness.EnrollAsync();

        await harness.Dispatcher.SendAsync(new EarnPointsCommand(
            harness.CompanyId, harness.CustomerId, 1000m, "ZAR", "sale-1", Guid.NewGuid()));

        RedeemOutcome outcome = await harness.Dispatcher.SendAsync(new RedeemPointsCommand(
            harness.CompanyId, harness.CustomerId, 200m, "reward-1", Guid.NewGuid()));

        outcome.Queued.Should().BeFalse();
        outcome.NewBalance.Should().Be(800m);

        IReadOnlyList<LoyaltyTransactionEntry> history = await harness.Dispatcher.QueryAsync(
            new ListTransactionsQuery(harness.CustomerId, 10));
        history.Should().HaveCount(2);
    }

    [Fact]
    public async Task Same_key_twice_applies_once_at_both_ledgers()
    {
        await using LoyaltyHarness harness = await LoyaltyHarness.CreateAsync(fixture);
        await harness.EnrollAsync();

        var key = Guid.NewGuid();
        EarnOutcome first = await harness.Dispatcher.SendAsync(new EarnPointsCommand(
            harness.CompanyId, harness.CustomerId, 100m, "ZAR", "sale-1", key));
        EarnOutcome second = await harness.Dispatcher.SendAsync(new EarnPointsCommand(
            harness.CompanyId, harness.CustomerId, 100m, "ZAR", "sale-1", key));

        second.TransactionId.Should().Be(first.TransactionId);

        int rows = await harness.Context.LoyaltyTransactions.CountAsync();
        rows.Should().Be(1);

        OrbitBalanceResult ledger = await harness.Orbit.GetBalanceAsync(
            (await harness.Members.FindByCustomerAsync(harness.CustomerId))!.OrbitMemberId);
        ledger.Balance.Should().Be(100m, "Orbit applied the keyed earn exactly once");
    }

    [Fact]
    public async Task Same_key_different_body_is_a_conflict()
    {
        await using LoyaltyHarness harness = await LoyaltyHarness.CreateAsync(fixture);
        await harness.EnrollAsync();

        var key = Guid.NewGuid();
        await harness.Dispatcher.SendAsync(new EarnPointsCommand(
            harness.CompanyId, harness.CustomerId, 100m, "ZAR", "sale-1", key));

        Func<Task> act = () => harness.Dispatcher.SendAsync(new EarnPointsCommand(
            harness.CompanyId, harness.CustomerId, 200m, "ZAR", "sale-2", key));

        await act.Should().ThrowAsync<DuplicateLoyaltyTransactionException>();
    }

    [Fact]
    public async Task Two_concurrent_burns_on_100_only_one_succeeds()
    {
        await using LoyaltyHarness harness = await LoyaltyHarness.CreateAsync(fixture);
        await harness.EnrollAsync();

        await harness.Dispatcher.SendAsync(new EarnPointsCommand(
            harness.CompanyId, harness.CustomerId, 100m, "ZAR", "sale-1", Guid.NewGuid()));

        // Two independent dispatchers over the same database: one shared context cannot hold
        // two real transactions at once, and the gate under test is Orbit's ledger anyway.
        await using LoyaltyFork first = harness.Fork();
        await using LoyaltyFork second = harness.Fork();

        Task<RedeemOutcome> attemptA = first.Dispatcher.SendAsync(new RedeemPointsCommand(
            harness.CompanyId, harness.CustomerId, 60m, "reward-1", Guid.NewGuid()));
        Task<RedeemOutcome> attemptB = second.Dispatcher.SendAsync(new RedeemPointsCommand(
            harness.CompanyId, harness.CustomerId, 60m, "reward-2", Guid.NewGuid()));

        int wins = 0;
        foreach (Task<RedeemOutcome> attempt in new[] { attemptA, attemptB })
        {
            try
            {
                await attempt;
                wins++;
            }
            catch (InsufficientPointsException)
            {
            }
        }

        wins.Should().Be(1);
    }

    [Fact]
    public async Task Outage_queues_then_retry_confirms_exactly_once()
    {
        await using LoyaltyHarness harness = await LoyaltyHarness.CreateAsync(fixture);
        await harness.EnrollAsync();

        harness.Orbit.MakeUnavailable();
        EarnOutcome queued = await harness.Dispatcher.SendAsync(new EarnPointsCommand(
            harness.CompanyId, harness.CustomerId, 150m, "ZAR", "sale-1", Guid.NewGuid()));
        queued.Queued.Should().BeTrue();

        LoyaltyTransaction? pending = await harness.Transactions.FindAsync(queued.TransactionId);
        pending!.Status.Should().Be(TransactionStatus.QueuedForRetry);

        harness.Orbit.MakeAvailable();
        RetryDisposition disposition = await harness.Dispatcher.SendAsync(
            new RetryLoyaltyTransactionCommand(queued.TransactionId));

        disposition.Should().Be(RetryDisposition.Confirmed);

        BalanceEntry balance = await harness.Dispatcher.QueryAsync(
            new GetBalanceQuery(harness.CustomerId));
        balance.Balance.Should().Be(150m);
    }

    [Fact]
    public async Task Webhook_updates_cache_for_a_known_member_and_ignores_unknown()
    {
        await using LoyaltyHarness harness = await LoyaltyHarness.CreateAsync(fixture);
        await harness.EnrollAsync();

        string orbitId = (await harness.Members.FindByCustomerAsync(harness.CustomerId))!.OrbitMemberId;

        await harness.Dispatcher.SendAsync(new ProcessLoyaltyWebhookCommand(
            harness.CompanyId, orbitId, 750m, "gold", "balance.adjusted"));

        BalanceEntry balance = await harness.Dispatcher.QueryAsync(
            new GetBalanceQuery(harness.CustomerId));
        balance.Balance.Should().Be(750m);

        // Unknown members never conjure rows.
        await harness.Dispatcher.SendAsync(new ProcessLoyaltyWebhookCommand(
            harness.CompanyId, "orbit-ghost", 10m, null, "balance.adjusted"));

        int members = await harness.Context.LoyaltyMembers.CountAsync();
        members.Should().Be(1);
    }

    [Fact]
    public async Task Catalogue_sync_caches_tiers_and_rewards()
    {
        await using LoyaltyHarness harness = await LoyaltyHarness.CreateAsync(fixture);
        await harness.EnrollAsync();

        CatalogueSyncOutcome outcome = await harness.Dispatcher.SendAsync(
            new SyncCatalogueCommand(harness.CompanyId));

        outcome.Tiers.Should().Be(3);
        outcome.Rewards.Should().Be(2);

        IReadOnlyList<TierEntry> tiers = await harness.Dispatcher.QueryAsync(
            new ListTiersQuery(harness.CompanyId));
        tiers.Should().HaveCount(3);

        MemberTierEntry progress = await harness.Dispatcher.QueryAsync(
            new GetMemberTierQuery(harness.CustomerId));
        progress.NextTierId.Should().Be("silver");
        progress.PointsToNext.Should().Be(1000m);
    }

    [Fact]
    public async Task Idempotency_key_is_unique_per_company_in_the_database()
    {
        await using LoyaltyHarness harness = await LoyaltyHarness.CreateAsync(fixture);
        await harness.EnrollAsync();

        var key = Guid.NewGuid();
        await harness.Dispatcher.SendAsync(new EarnPointsCommand(
            harness.CompanyId, harness.CustomerId, 100m, "ZAR", "sale-1", key));

        // Same key, straight at the table: the unique index refuses.
        harness.Context.LoyaltyTransactions.Add(new LoyaltyTransaction(
            harness.TenantId, harness.CompanyId, harness.CustomerId, TransactionType.Earn,
            100m, "ZAR", key, "sale-2", harness.Clock.UtcNow));

        Func<Task> act = () => harness.Context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
