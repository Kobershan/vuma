using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;
using Xunit;
using FluentAssertions;
using NSubstitute;

namespace VumaRetail.IntegrationTests.Stage06c;

[Trait("Category", "Integration")]
[Trait("Stage", "06C")]
[Trait("Requirement", "06C-06")]
public sealed class _06C06_ProvisioningTests : IAsyncDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly string _connectionString;
    
    public _06C06_ProvisioningTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _connectionString = fixture.AdminConnectionString;
    }
    
    [Fact]
    public async Task Provisioning_fails_if_registry_migration_not_applied()
    {
        Guid tenantId = UuidV7.NewGuid();
        string connectionString = await _fixture.CreateEmptyDatabaseAsync();
        
        await using (VumaRegistryDbContext context = CreateContext(connectionString, tenantId))
        {
            var company = Company.Create(
                tenantId, 
                "pending", 
                "Pending Company", 
                "Pending Company", 
                "ZAR", 
                "en-ZA", 
                "PD-");
                
            var provisioner = new CompanyProvisioner(
                context, 
                MockUnitOfWork(), 
                new ICompanyProvisioningStep[] {
                    new MockStep("create", CompanyLifecycleState.Seeding),
                    new MockStep("migrate", CompanyLifecycleState.Registered)
                }, 
                MockConnectionResolver(),
                null);
                
            Func<Task> provision = () => provisioner.ProvisionAsync(company, CancellationToken.None);
            await provision.Should().ThrowAsync<InvalidOperationException>();
        }
    }
    
    [Fact]
    public async Task Provisioning_succeeds_with_correct_migrations()
    {
        Guid tenantId = UuidV7.NewGuid();
        string connectionString = await _fixture.CreateEmptyDatabaseAsync();
        
        await using (VumaRegistryDbContext context = CreateContext(connectionString, tenantId))
        {
            await context.Database.MigrateAsync();
            
            var company = Company.Create(
                tenantId, 
                "test", 
                "Test Company", 
                "Test Company", 
                "ZAR", 
                "en-ZA", 
                "TC-");
                
            var provisioner = new CompanyProvisioner(
                context, 
                MockUnitOfWork(), 
                new ICompanyProvisioningStep[] {
                    new MockStep("create", CompanyLifecycleState.Seeding),
                    new MockStep("migrate", CompanyLifecycleState.Registered)
                }, 
                MockConnectionResolver(),
                null);
                
            await provisioner.ProvisionAsync(company, CancellationToken.None);
            
            company.LifecycleState.Should().Be(CompanyLifecycleState.Registered);
            company.IsActive.Should().BeFalse();
        }
    }
    
    private static VumaRegistryDbContext CreateContext(string connectionString, Guid? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<VumaRegistryDbContext>()
            .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", "registry"))
            .UseSnakeCaseNamingConvention().Options;
            
        return new VumaRegistryDbContext(options, new TestTenantContext(tenantId ?? Guid.Empty));
    }
    
    private sealed class MockStep(string name, CompanyLifecycleState completedState) : ICompanyProvisioningStep
    {
        public string Name => name;
        public CompanyLifecycleState CompletedState => completedState;
        
        public Task ExecuteAsync(Company company, CancellationToken cancellationToken)
        {
            company.SetLifecycle(completedState);
            return Task.CompletedTask;
        }
    }
    
    private static IUnitOfWork MockUnitOfWork()
    {
        return NSubstitute.Substitute.For<IUnitOfWork>();
    }
    
    private static ICompanyConnectionResolver MockConnectionResolver()
    {
        var resolver = NSubstitute.Substitute.For<ICompanyConnectionResolver>();
        resolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CompanyAccessMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CompanyConnection(Guid.NewGuid(), Guid.NewGuid(), "test-secret", 1L)));
        return resolver;
    }
    
    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public Guid? StoreId => null;
        public bool IsFilterBypassed => false;
        public void SetTenant(Guid id, Guid? storeId = null) { }
        public IDisposable BypassTenantFilter(string reason) => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
    
    public ValueTask DisposeAsync()
    {
        return new ValueTask(Task.CompletedTask);
    }
}
