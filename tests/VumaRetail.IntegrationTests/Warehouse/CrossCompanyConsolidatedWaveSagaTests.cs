using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Warehouse.Commands;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Warehouse;

/// <summary>Runs each consolidated-wave saga leg against a real company database.</summary>
[Trait("Category", "Integration")]
[Trait("Stage", "13B")]
[Collection(PostgresCollection.Name)]
public sealed class CrossCompanyConsolidatedWaveSagaTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Two_company_wave_is_created_idempotently_and_compensated_per_company()
    {
        string firstDatabase = await fixture.CreateDatabaseAsync();
        string secondDatabase = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();
        Guid firstCompany = UuidV7.NewGuid();
        Guid secondCompany = UuidV7.NewGuid();
        Guid firstWave = UuidV7.NewGuid();
        Guid secondWave = UuidV7.NewGuid();

        await using ServiceProvider provider = BuildProvider(
            tenantId,
            new Dictionary<Guid, string> { [firstCompany] = firstDatabase, [secondCompany] = secondDatabase });
        var dispatcher = new ConsolidatedWaveSagaLegDispatcher(provider.GetRequiredService<IServiceScopeFactory>());
        CrossCompanyConsolidatedWavePayload payload = new(
            tenantId,
            null,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            "City",
            "Durban",
            [
                new(firstCompany, UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid(), null, 2m, "EA", "Each", "Durban"),
                new(secondCompany, UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid(), null, UuidV7.NewGuid(), 3m, "EA", "Each", "Durban")
            ],
            new Dictionary<Guid, Guid> { [firstCompany] = firstWave, [secondCompany] = secondWave });
        SagaIntent intent = SagaIntent.Create(
            tenantId,
            CrossCompanyConsolidatedWaveSaga.IntentType,
            "integration-cross-company-wave",
            TestClock.DefaultStart,
            JsonSerializer.Serialize(payload));
        SagaLeg firstLeg = intent.Legs.Count == 0
            ? AddLegs(intent, firstCompany, secondCompany).First()
            : intent.Legs.First();

        await dispatcher.DispatchAsync(intent, firstLeg);
        SagaLeg secondLeg = intent.Legs.Single(leg => leg.CompanyId == secondCompany);
        await dispatcher.DispatchAsync(intent, secondLeg);
        await dispatcher.DispatchAsync(intent, firstLeg);

        await AssertWaveAsync(firstDatabase, tenantId, firstWave, expectedTasks: 1);
        await AssertWaveAsync(secondDatabase, tenantId, secondWave, expectedTasks: 1);

        await dispatcher.CompensateAsync(intent, firstLeg);
        await dispatcher.CompensateAsync(intent, secondLeg);
        await AssertCancelledWaveAsync(firstDatabase, tenantId, firstWave);
        await AssertCancelledWaveAsync(secondDatabase, tenantId, secondWave);
    }

    private static IEnumerable<SagaLeg> AddLegs(SagaIntent intent, Guid firstCompany, Guid secondCompany)
    {
        intent.AddLeg(firstCompany);
        intent.AddLeg(secondCompany);
        return intent.Legs;
    }

    private static ServiceProvider BuildProvider(Guid tenantId, IReadOnlyDictionary<Guid, string> databases)
    {
        ServiceCollection services = new();
        services.AddScoped<ITenantContext, TestTenantContext>();
        services.AddScoped<ICompanyContext, AmbientCompanyContext>();
        services.AddScoped<ICompanyDbContextFactory>(services =>
            new TestCompanyDbContextFactory(
                services.GetRequiredService<ITenantContext>(),
                services.GetRequiredService<ICompanyContext>(),
                databases));
        return services.BuildServiceProvider();
    }

    private static async Task AssertWaveAsync(string database, Guid tenantId, Guid waveId, int expectedTasks)
    {
        await using VumaRetailDbContext context = TestDbContextFactory.For(
            database, tenant: TestTenantContext.For(tenantId));
        (await context.PickWaves.CountAsync(wave => wave.Id == waveId)).Should().Be(1);
        (await context.PickTasks.CountAsync(task => task.PickWaveId == waveId)).Should().Be(expectedTasks);
        (await context.PickWaveLineBreakdowns.CountAsync(breakdown =>
            context.PickTasks.Any(task => task.Id == breakdown.PickWaveLineId && task.PickWaveId == waveId)))
            .Should().Be(1);
    }

    private static async Task AssertCancelledWaveAsync(string database, Guid tenantId, Guid waveId)
    {
        await using VumaRetailDbContext context = TestDbContextFactory.For(
            database, tenant: TestTenantContext.For(tenantId));
        (await context.PickWaves.SingleAsync(wave => wave.Id == waveId)).Status
            .Should().Be(PickWaveStatus.Cancelled);
    }

    private sealed class TestCompanyDbContextFactory(
        ITenantContext tenant,
        ICompanyContext company,
        IReadOnlyDictionary<Guid, string> databases) : ICompanyDbContextFactory
    {
        public Task<VumaRetailDbContext> CreateAsync(CancellationToken cancellationToken = default)
            => CreateAsync(CompanyAccessMode.Write, cancellationToken);

        public Task<VumaRetailDbContext> CreateAsync(
            CompanyAccessMode access,
            CancellationToken cancellationToken = default)
        {
            string database = databases[company.RequireCompany()];
            return Task.FromResult<VumaRetailDbContext>(TestDbContextFactory.For(
                database,
                tenant: tenant,
                principal: new TestPrincipalAccessor("system:test-wave")));
        }
    }
}
