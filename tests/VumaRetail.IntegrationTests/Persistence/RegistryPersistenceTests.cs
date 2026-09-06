using Microsoft.EntityFrameworkCore;
using Npgsql;
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Sync;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class RegistryPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Company_with_pending_model_changes_is_refused_until_migration_is_current()
    {
        Guid tenantId = UuidV7.NewGuid();
        string companyConnectionString = await fixture.CreateEmptyDatabaseAsync();
        string registryConnectionString = await fixture.CreateEmptyDatabaseAsync();
        Guid companyId;

        await using (VumaRegistryDbContext registry = For(registryConnectionString, tenantId))
        {
            await registry.Database.MigrateAsync();
            Company company = Company.Create(tenantId, "pending", "Pending Company", "Pending Company", "ZAR", "en-ZA", "PD");
            company.SetConnectionSecretRef("test-company-connection");
            company.SetLifecycle(CompanyLifecycleState.Seeding);
            company.SetLifecycle(CompanyLifecycleState.Registered);
            company.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
            company.SetMigration(0, "Pending");
            company.RecordProvisioningFailure("Company migration failed; retry pending migration '20260809200337_InitialCreate'.");
            registry.Companies.Add(company);
            await registry.SaveChangesAsync();
            companyId = company.Id;
        }

        await using (VumaRetailDbContext companyDatabase = TestDbContextFactory.For(companyConnectionString))
        {
            (await companyDatabase.Database.GetPendingMigrationsAsync())
                .Should().Contain("20260809200337_InitialCreate");
        }

        await using (VumaRegistryDbContext registry = For(registryConnectionString, tenantId))
        {
            CompanyServingGuard guard = new(registry);

            Func<Task> access = () => guard.EnsureAccessibleAsync(
                tenantId,
                companyId,
                CompanyAccessMode.Read);

            var exception = await access.Should().ThrowAsync<InvalidOperationException>();
            exception.Which.Message.Should().Be(
                "COMPANY_MIGRATION_REQUIRED: company 'pending' is not served until Company migration failed; retry pending migration '20260809200337_InitialCreate'.");
            exception.Which.Message.Should().NotContain("Host=");
            exception.Which.Message.Should().NotContain("Password=");
        }
    }

    [Fact]
    public async Task Three_provisioned_company_databases_are_physically_isolated()
    {
        Guid tenantId = Guid.Parse("01900000-0000-7000-8000-0000000006c0");
        Guid operationId = Guid.Parse("01900000-0000-7000-8000-0000000006c1");
        string[] companyCodes = ["hardware", "distribution", "groceries"];
        Guid[] entityIds =
        [
            Guid.Parse("01900000-0000-7000-8000-0000000006c2"),
            Guid.Parse("01900000-0000-7000-8000-0000000006c3"),
            Guid.Parse("01900000-0000-7000-8000-0000000006c4"),
        ];
        string[] databaseNames = new string[companyCodes.Length];

        for (int index = 0; index < companyCodes.Length; index++)
        {
            string connectionString = await fixture.CreateDatabaseAsync();
            await using VumaRetailDbContext context = TestDbContextFactory.For(connectionString);

            context.OutboxMessages.Add(OutboxMessage.Capture(
                tenantId,
                storeId: null,
                operationId,
                sourceNode: $"company:{companyCodes[index]}",
                entityType: "Stage06cIsolationProbe",
                entityId: entityIds[index],
                operation: SyncOperationKind.Upsert,
                scope: ReplicationScope.StoreToCloud,
                conflictPolicy: ConflictPolicy.LastWriterWins,
                stamp: new HlcStamp(index + 1, 0, companyCodes[index]),
                payload: $"{{\"companyCode\":\"{companyCodes[index]}\"}}",
                occurredAt: DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();

            databaseNames[index] = await context.Database.SqlQuery<string>($"SELECT current_database() AS \"Value\"").SingleAsync();
            (await context.OutboxMessages.SingleAsync(message => message.OperationId == operationId))
                .Payload.Should().Be($"{{\"companyCode\":\"{companyCodes[index]}\"}}");
        }

        databaseNames.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Registry_migration_persists_saga_records_and_is_tenant_scoped()
    {
        string connectionString = await fixture.CreateEmptyDatabaseAsync();
        Guid tenantA = UuidV7.NewGuid();
        Guid tenantB = UuidV7.NewGuid();
        await using (VumaRegistryDbContext context = For(connectionString, tenantA))
        {
            await context.Database.MigrateAsync();
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
            context.Add(new RegistryOutboxMessage(tenantB, "saga.dispatch", "{}", DateTimeOffset.UtcNow, "tenant-b"));
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();
            (await context.SagaIntents.SingleAsync(x => x.TenantId == tenantA)).Payload.Should().NotContain("redacted");
            (await context.RegistryOutboxMessages.CountAsync()).Should().Be(1);
            (await context.Database.GetAppliedMigrationsAsync()).Should().Contain("20260828100000_RegistryGroupsAndSagas");
        }

        await using (VumaRegistryDbContext restarted = For(connectionString, tenantA))
        {
            (await restarted.SagaIntents.CountAsync()).Should().Be(1);
            (await restarted.RegistryOutboxMessages.CountAsync()).Should().Be(1);
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

        await using (Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync())
        {
            await context.Database.ExecuteSqlRawAsync("INSERT INTO registry.outbox_messages (id, tenant_id, type, payload, idempotency_key, created_at, attempts, operation_stamp) VALUES ({0}, {1}, 'rollback', '{{}}', 'rollback-key', now(), 0, '0-0-registry')", UuidV7.NewGuid(), tenantId);
            await transaction.RollbackAsync();
        }
        (await context.RegistryOutboxMessages.AnyAsync(x => x.IdempotencyKey == "rollback-key")).Should().BeFalse();
    }

    private static VumaRegistryDbContext For(string connectionString, Guid? tenantId = null)
    {
        DbContextOptions<VumaRegistryDbContext> options = new DbContextOptionsBuilder<VumaRegistryDbContext>()
            .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", "registry"))
            .UseSnakeCaseNamingConvention().Options;
        return new VumaRegistryDbContext(options, new TestTenantContext(tenantId ?? Guid.Empty));
    }

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public Guid? StoreId => null;
        public bool IsFilterBypassed => tenantId == Guid.Empty;
        public void SetTenant(Guid id, Guid? storeId = null) { }
        public IDisposable BypassTenantFilter(string reason) => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}
