using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;
using Xunit;
using FluentAssertions;

namespace VumaRetail.UnitTests.Stage06c;

[Trait("Category", "Unit")]
[Trait("Stage", "06C")]
[Trait("Requirement", "06C-05")]
public sealed class _06C05_FanoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly MockDbContextFactory Factory = new();

    [Fact]
    public async Task Fanout_returns_partial_results_on_failure()
    {
        var clock = new TestClock();
        var fanout = new CompanyFanOut(Factory, clock, 2);
        
        Guid companyA = UuidV7.NewGuid();
        Guid companyB = UuidV7.NewGuid();
        Guid unknownCompany = UuidV7.NewGuid();
        
        var companies = new List<Guid> { companyA, companyB, unknownCompany };
        
        Func<Guid, CancellationToken, Task<string>> read = 
            async (id, _) => 
                id == unknownCompany 
                    ? throw new InvalidOperationException("company unknown") 
                    : $"result-{id}";
        
        var results = await fanout.ReadAsync<string>(companies, read, CancellationToken.None);
        
        results.Should().HaveCount(3);
        results[0].Succeeded.Should().BeTrue();
        results[0].Value.Should().Be($"result-{companyA}");
        results[1].Succeeded.Should().BeTrue();
        results[1].Value.Should().Be($"result-{companyB}");
        results[2].Succeeded.Should().BeFalse();
        results[2].Error.Should().Be("Company read failed.");
    }
    
    [Fact]
    public async Task Fanout_handles_cancellation()
    {
        var cts = new CancellationTokenSource();
        var fanout = new CompanyFanOut(Factory, new TestClock(), 2);
        
        Func<Guid, CancellationToken, Task<string>> read = async (_, token) =>
        {
            await Task.Delay(1000, token);
            return "done";
        };
        
        cts.CancelAfter(100);
        
        Func<Task> action = () => fanout.ReadAsync<string>(
            new List<Guid> { UuidV7.NewGuid() }, 
            read, 
            cts.Token);
            
        await action.Should().ThrowAsync<OperationCanceledException>();
    }
    
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

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
}
