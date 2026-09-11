using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Registry;

/// <summary>Exercises the durable Stage 06d coordinator against the real registry schema.</summary>
[Trait("Category", "Integration")]
[Trait("Stage", "06D")]
[Collection(PostgresCollection.Name)]
public sealed class SagaCoordinatorTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Failed_leg_is_retried_with_the_same_identity_until_it_applies_once()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();
        Guid companyId;

        await using VumaRegistryDbContext registry = TestDbContextFactory.ForRegistry(
            connectionString, TestTenantContext.For(tenantId));
        Company company = Company.Create(tenantId, "SAGA", "Saga Company", "Saga Company", "ZAR", "en-ZA", "SG-");
        registry.Companies.Add(company);
        await registry.SaveChangesAsync();
        companyId = company.Id;

        var dispatcher = new FailsThenSucceedsDispatcher(failures: 3);
        var coordinator = new SagaCoordinator(registry, new TestClock(), [dispatcher]);
        SagaIntent intent = SagaIntent.Create(tenantId, "test.saga", "saga-retry-1", TestClock.DefaultStart);
        intent.Authorize(UuidV7.NewGuid(), "test:operator", new HlcStamp(1, 0, "test").ToString());
        intent.AddLeg(companyId);

        SagaResult first = await coordinator.ExecuteAsync(intent);
        first.FinalState.Should().Be(SagaIntentState.InProgress);
        first.Legs.Should().ContainSingle().Which.State.Should().Be(SagaLegState.Failed);

        for (int retry = 0; retry < 3; retry++)
        {
            await coordinator.RetryLegAsync(intent.Id, intent.Legs.Single().LegId);
        }

        SagaIntent? persisted = await coordinator.GetAsync(intent.Id);
        persisted.Should().NotBeNull();
        persisted = persisted!;
        persisted.State.Should().Be(SagaIntentState.Completed);
        persisted.Legs.Should().ContainSingle().Which.State.Should().Be(SagaLegState.Acknowledged);
        persisted.Legs.Single().Attempts.Should().Be(4);
        dispatcher.AppliedLegs.Should().ContainSingle().Which.Should().Be((intent.Id, intent.Legs.Single().LegId));

        SagaResult replay = await coordinator.ExecuteAsync(intent);
        replay.FinalState.Should().Be(SagaIntentState.Completed);
        dispatcher.CallCount.Should().Be(4, "the recorded intent is the idempotency boundary");
    }

    private sealed class FailsThenSucceedsDispatcher(int failures) : ISagaLegDispatcher
    {
        private int _remainingFailures = failures;

        public int CallCount { get; private set; }
        public List<(Guid IntentId, Guid LegId)> AppliedLegs { get; } = [];

        public bool CanDispatch(string intentType) => intentType == "test.saga";

        public Task DispatchAsync(SagaIntent intent, SagaLeg leg, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_remainingFailures-- > 0) throw new InvalidOperationException("company database is temporarily unavailable");
            AppliedLegs.Add((intent.Id, leg.LegId));
            return Task.CompletedTask;
        }

        public Task CompensateAsync(SagaIntent intent, SagaLeg leg, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
