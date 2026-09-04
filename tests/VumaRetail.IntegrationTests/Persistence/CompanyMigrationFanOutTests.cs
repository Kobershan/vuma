using Microsoft.EntityFrameworkCore;
using Npgsql;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class CompanyMigrationFanOutTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Migration_fan_out_succeeds_for_reachable_companies_and_records_failure_for_unreachable_sibling()
    {
        // Arrange: a tenant with three companies in registry; two have reachable databases, one does not.
        Guid tenantId = UuidV7.NewGuid();
        string registryConnectionString = await fixture.CreateEmptyDatabaseAsync();
        string reachableDbA = await fixture.CreateDatabaseAsync();
        string reachableDbB = await fixture.CreateDatabaseAsync();

        await using (VumaRegistryDbContext registry = For(registryConnectionString, tenantId))
        {
            await registry.Database.MigrateAsync();

            Company companyA = Company.Create(tenantId, "company-a", "Company A", "Company A", "ZAR", "en-ZA", "A");
            companyA.SetConnectionSecretRef("secret://company-a");
            companyA.SetLifecycle(CompanyLifecycleState.Seeding); // <-- ADDED: Must transition through Seeding first
            companyA.SetLifecycle(CompanyLifecycleState.Registered);
            companyA.SetMigration(0, "Pending");

            Company companyB = Company.Create(tenantId, "company-b", "Company B", "Company B", "ZAR", "en-ZA", "B");
            companyB.SetConnectionSecretRef("secret://company-b");
            companyB.SetLifecycle(CompanyLifecycleState.Seeding); // <-- ADDED
            companyB.SetLifecycle(CompanyLifecycleState.Registered);
            companyB.SetMigration(0, "Pending");

            Company companyC = Company.Create(tenantId, "company-c", "Company C", "Company C", "ZAR", "en-ZA", "C");
            companyC.SetConnectionSecretRef("secret://company-c");
            companyC.SetLifecycle(CompanyLifecycleState.Seeding); // <-- ADDED
            companyC.SetLifecycle(CompanyLifecycleState.Registered);
            companyC.SetMigration(0, "Pending");

            registry.Companies.AddRange(companyA, companyB, companyC);
            await registry.SaveChangesAsync();
        }

        var secretStore = new TestCompanyConnectionSecretStore(new Dictionary<string, string>
        {
            ["secret://company-a"] = reachableDbA,
            ["secret://company-b"] = reachableDbB,
            ["secret://company-c"] = "Host=unreachable.invalid;Port=5432;Database=none;Username=x;Password=x",
        });

        await using (VumaRegistryDbContext registry = For(registryConnectionString, tenantId))
        {
            var runner = new CompanyMigrationRunner(registry, secretStore, registry);

            // Act
            IReadOnlyList<CompanyMigrationResult> results = await runner.MigrateAsync(tenantId);

            // Assert
            results.Should().HaveCount(3);

            CompanyMigrationResult resultA = results.Single(r => r.CompanyId == registry.Companies.Single(c => c.Code == "company-a").Id);
            CompanyMigrationResult resultB = results.Single(r => r.CompanyId == registry.Companies.Single(c => c.Code == "company-b").Id);
            CompanyMigrationResult resultC = results.Single(r => r.CompanyId == registry.Companies.Single(c => c.Code == "company-c").Id);

            resultA.Succeeded.Should().BeTrue("reachable company A should migrate successfully");
            resultA.Error.Should().BeNull();
            resultA.SchemaVersion.Should().BeGreaterThan(0);

            resultB.Succeeded.Should().BeTrue("reachable company B should migrate successfully");
            resultB.Error.Should().BeNull();
            resultB.SchemaVersion.Should().BeGreaterThan(0);

            resultC.Succeeded.Should().BeFalse("unreachable company C should fail migration");
            resultC.Error.Should().Contain("Company migration failed");
            resultC.SchemaVersion.Should().Be(0);

            // Verify registry state is updated
            var updatedA = await registry.Companies.SingleAsync(c => c.Code == "company-a");
            updatedA.MigrationState.Should().Be("Current");
            updatedA.ProvisioningError.Should().BeNull();

            var updatedB = await registry.Companies.SingleAsync(c => c.Code == "company-b");
            updatedB.MigrationState.Should().Be("Current");
            updatedB.ProvisioningError.Should().BeNull();

            var updatedC = await registry.Companies.SingleAsync(c => c.Code == "company-c");
            updatedC.MigrationState.Should().Be("Pending");
            updatedC.ProvisioningError.Should().Contain("Company migration failed");
        }
    }

    [Fact]
    public async Task Migration_fan_out_resumes_from_partial_failure_and_skips_already_current_companies()
    {
        // Arrange: two companies, one already Current, one Pending.
        Guid tenantId = UuidV7.NewGuid();
        string registryConnectionString = await fixture.CreateEmptyDatabaseAsync();
        string reachableDb = await fixture.CreateDatabaseAsync();

        await using (VumaRegistryDbContext registry = For(registryConnectionString, tenantId))
        {
            await registry.Database.MigrateAsync();

            Company current = Company.Create(tenantId, "current", "Current Company", "Current Company", "ZAR", "en-ZA", "CUR");
            current.SetConnectionSecretRef("secret://current");
            current.SetLifecycle(CompanyLifecycleState.Seeding); // <-- ADDED
            current.SetLifecycle(CompanyLifecycleState.Registered);
            current.SetMigration(1, "Current");

            Company pending = Company.Create(tenantId, "pending", "Pending Company", "Pending Company", "ZAR", "en-ZA", "PEN");
            pending.SetConnectionSecretRef("secret://pending");
            pending.SetLifecycle(CompanyLifecycleState.Seeding); // <-- ADDED
            pending.SetLifecycle(CompanyLifecycleState.Registered);
            pending.SetMigration(0, "Pending");

            registry.Companies.AddRange(current, pending);
            await registry.SaveChangesAsync();
        }

        var secretStore = new TestCompanyConnectionSecretStore(new Dictionary<string, string>
        {
            ["secret://current"] = reachableDb,
            ["secret://pending"] = reachableDb,
        });

        await using (VumaRegistryDbContext registry = For(registryConnectionString, tenantId))
        {
            var runner = new CompanyMigrationRunner(registry, secretStore, registry);

            // Act
            IReadOnlyList<CompanyMigrationResult> results = await runner.MigrateAsync(tenantId);

            // Assert
            results.Should().HaveCount(2);

            CompanyMigrationResult currentResult = results.Single(r => r.CompanyId == registry.Companies.Single(c => c.Code == "current").Id);
            currentResult.Succeeded.Should().BeTrue();
            currentResult.SchemaVersion.Should().BeGreaterOrEqualTo(1);

            CompanyMigrationResult pendingResult = results.Single(r => r.CompanyId == registry.Companies.Single(c => c.Code == "pending").Id);
            pendingResult.Succeeded.Should().BeTrue();
            pendingResult.SchemaVersion.Should().BeGreaterThan(0);
        }
    }

    private static VumaRegistryDbContext For(string connectionString, Guid tenantId)
    {
        DbContextOptions<VumaRegistryDbContext> options = new DbContextOptionsBuilder<VumaRegistryDbContext>()
            .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", "registry"))
            .UseSnakeCaseNamingConvention().Options;
        return new VumaRegistryDbContext(options, TestTenantContext.For(tenantId));
    }

    private sealed class TestCompanyConnectionSecretStore(Dictionary<string, string> secrets) : ICompanyConnectionSecretStore
    {
        public Task<string> ResolveAsync(string secretReference, CancellationToken cancellationToken = default)
        {
            if (secrets.TryGetValue(secretReference, out var value))
            {
                return Task.FromResult(value);
            }
            throw new InvalidOperationException($"Secret not found: {secretReference}");
        }
    }
}