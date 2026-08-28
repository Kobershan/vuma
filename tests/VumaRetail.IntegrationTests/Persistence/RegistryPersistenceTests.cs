using Microsoft.EntityFrameworkCore;
using Npgsql;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class RegistryPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Registry_migration_persists_saga_records_and_is_tenant_scoped()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync();
        await using (VumaRegistryDbContext context = For(connectionString))
        {
            await context.Database.MigrateAsync();
            Guid tenantA = UuidV7.NewGuid();
            Guid tenantB = UuidV7.NewGuid();
            Company company = Company.Create(tenantA, "A", "Company A", "Company A", "ZAR", "en-ZA", "A-");
            CompanyGroup group = CompanyGroup.Create(tenantA, "Group A");
            group.AddMember(company.Id);
            SagaIntent intent = SagaIntent.Create(tenantA, "split-sale", "request-1", DateTimeOffset.UtcNow,
                "{\"amount\":10,\"secret\":\"redacted\"}");
            intent.Authorize(UuidV7.NewGuid(), "operator:a", new HlcStamp(1, 0, "registry").ToString());
            intent.AddLeg(company.Id);
            intent.Start("worker:registry");
            context.AddRange(company, group, intent,
                new RegistryOutboxMessage(tenantA, "saga.dispatch", "{\"intentId\":\"x\"}", DateTimeOffset.UtcNow, "dispatch-1"));
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();
            (await context.SagaIntents.SingleAsync(x => x.TenantId == tenantA)).Payload.Should().NotContain("redacted");
            (await context.CompanyGroups.CountAsync(x => x.TenantId == tenantB)).Should().Be(0);
            (await context.Database.GetAppliedMigrationsAsync()).Should().Contain("20260828100000_RegistryGroupsAndSagas");
        }
    }

    [Fact]
    public async Task Registry_outbox_idempotency_and_transaction_rollback_are_database_enforced()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync();
        await using VumaRegistryDbContext context = For(connectionString);
        await context.Database.MigrateAsync();
        Guid tenantId = UuidV7.NewGuid();
        context.Add(new RegistryOutboxMessage(tenantId, "saga.dispatch", "{}", DateTimeOffset.UtcNow, "same-key"));
        await context.SaveChangesAsync();
        context.Add(new RegistryOutboxMessage(tenantId, "saga.dispatch", "{}", DateTimeOffset.UtcNow, "same-key"));
        Func<Task> duplicate = () => context.SaveChangesAsync();
        await duplicate.Should().ThrowAsync<DbUpdateException>();
        context.ChangeTracker.Clear();
        (await context.RegistryOutboxMessages.CountAsync()).Should().Be(1);

        await context.Database.ExecuteSqlRawAsync("BEGIN");
        await context.Database.ExecuteSqlRawAsync("INSERT INTO registry.outbox_messages (id, tenant_id, type, payload, idempotency_key, created_at, attempts, operation_stamp) VALUES ({0}, {1}, 'rollback', '{{}}', 'rollback-key', now(), 0, '0-0-registry')", UuidV7.NewGuid(), tenantId);
        await context.Database.ExecuteSqlRawAsync("ROLLBACK");
        (await context.RegistryOutboxMessages.AnyAsync(x => x.IdempotencyKey == "rollback-key")).Should().BeFalse();
    }

    private static VumaRegistryDbContext For(string connectionString)
    {
        DbContextOptions<VumaRegistryDbContext> options = new DbContextOptionsBuilder<VumaRegistryDbContext>()
            .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", "registry"))
            .UseSnakeCaseNamingConvention().Options;
        return new VumaRegistryDbContext(options);
    }
}
