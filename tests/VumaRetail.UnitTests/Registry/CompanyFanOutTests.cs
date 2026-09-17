using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;

namespace VumaRetail.UnitTests.Registry;

public sealed class CompanyFanOutTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly MockDbContextFactory Factory = new();

    private sealed class MockDbContextFactory : IDbContextFactory<VumaRetail.Infrastructure.Persistence.VumaRegistryDbContext>
    {
        public VumaRetail.Infrastructure.Persistence.VumaRegistryDbContext CreateDbContext()
            => throw new NotImplementedException();

        public VumaRetail.Infrastructure.Persistence.VumaRegistryDbContext CreateDbContext(string? connectionString = null)
            => throw new NotImplementedException();

        public async ValueTask<VumaRetail.Infrastructure.Persistence.VumaRegistryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public VumaRetail.Infrastructure.Persistence.VumaRegistryDbContext Create()
            => throw new NotImplementedException();
    }

    [Fact]
    public async Task Returns_successes_and_a_named_failure_in_input_order_with_bounded_concurrency()
    {
        Guid first = Guid.NewGuid();
        Guid stopped = Guid.NewGuid();
        Guid third = Guid.NewGuid();
        int active = 0;
        int maximum = 0;
        var fanOut = new CompanyFanOut(Factory, new FixedClock(Now), maxConcurrency: 2);

        IReadOnlyList<FanOutResult<string>> results = await fanOut.ReadAsync(
            [first, stopped, third],
            async (companyId, cancellationToken) =>
            {
                int nowActive = Interlocked.Increment(ref active);
                InterlockedMax(ref maximum, nowActive);
                try
                {
                    await Task.Delay(15, cancellationToken);
                    if (companyId == stopped)
                    {
                        throw new InvalidOperationException("Host=secret;Password=do-not-leak");
                    }

                    return companyId.ToString("N");
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });

        results.Select(result => result.CompanyId).Should().Equal(first, stopped, third);
        results[0].Value.Should().Be(first.ToString("N"));
        results[1].Succeeded.Should().BeFalse();
        results[1].Error.Should().Be("Company read failed.");
        results[1].Error.Should().NotContain("secret");
        results[2].Value.Should().Be(third.ToString("N"));
        maximum.Should().Be(2);
    }

    [Fact]
    public async Task A_slow_company_becomes_a_named_timeout_and_siblings_complete()
    {
        Guid slow = Guid.NewGuid();
        Guid fast = Guid.NewGuid();
        var fanOut = new CompanyFanOut(Factory, new FixedClock(Now), maxConcurrency: 2, readTimeout: TimeSpan.FromMilliseconds(20));

        IReadOnlyList<FanOutResult<string>> results = await fanOut.ReadAsync(
            [slow, fast],
            async (companyId, cancellationToken) =>
            {
                if (companyId == slow)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return "available";
            });

        results[0].Error.Should().Be("Read timed out.");
        results[1].Value.Should().Be("available");
    }

    [Fact]
    public async Task Caller_cancellation_aborts_the_whole_fan_out()
    {
        using var cancellation = new CancellationTokenSource();
        var fanOut = new CompanyFanOut(Factory, new FixedClock(Now));
        Task read = fanOut.ReadAsync<object?>(
            [Guid.NewGuid(), Guid.NewGuid()],
            async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return null;
            },
            cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public async Task Empty_and_duplicate_company_ids_do_not_fan_out_twice()
    {
        Guid companyId = Guid.NewGuid();
        int calls = 0;
        var fanOut = new CompanyFanOut(Factory, new FixedClock(Now));

        IReadOnlyList<FanOutResult<int>> results = await fanOut.ReadAsync(
            [Guid.Empty, companyId, companyId],
            (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(7);
            });

        calls.Should().Be(1);
        results.Should().ContainSingle().Which.Value.Should().Be(7);
    }

    [Fact]
    public void Clearing_balances_debit_source_and_credit_target_per_outstanding_intent()
    {
        Guid from = Guid.NewGuid();
        Guid to = Guid.NewGuid();
        Guid idle = Guid.NewGuid();

        IReadOnlyList<CompanyClearingBalance> balances = CompanyFanOut.AggregateClearingBalances(
            [from, to, idle],
            [(from, to, 100m), (to, from, 40m)]);

        balances.Single(b => b.CompanyId == from).DebitAmount.Should().Be(100m);
        balances.Single(b => b.CompanyId == from).CreditAmount.Should().Be(40m);
        balances.Single(b => b.CompanyId == to).DebitAmount.Should().Be(40m);
        balances.Single(b => b.CompanyId == to).CreditAmount.Should().Be(100m);
        balances.Single(b => b.CompanyId == idle).DebitAmount.Should().Be(0m);
        balances.Single(b => b.CompanyId == idle).CreditAmount.Should().Be(0m);
        balances.Sum(b => b.DebitAmount).Should().Be(balances.Sum(b => b.CreditAmount));
    }

    [Fact]
    public void Clearing_balances_ignore_non_positive_amounts_and_unknown_companies()
    {
        Guid from = Guid.NewGuid();
        Guid to = Guid.NewGuid();

        IReadOnlyList<CompanyClearingBalance> balances = CompanyFanOut.AggregateClearingBalances(
            [from, to],
            [(from, to, 0m), (from, to, -5m), (Guid.NewGuid(), Guid.NewGuid(), 25m)]);

        balances.Should().HaveCount(2);
        balances.Sum(b => b.DebitAmount).Should().Be(0m);
        balances.Sum(b => b.CreditAmount).Should().Be(0m);
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref location);
            if (value <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref location, value, current) != current);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
