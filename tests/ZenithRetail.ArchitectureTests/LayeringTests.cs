using System.Reflection;
using NetArchTest.Rules;

namespace ZenithRetail.ArchitectureTests;

/// <summary>
/// CLAUDE.md §7 rule 1 — the dependency direction, checked rather than trusted.
/// </summary>
/// <remarks>
/// Project references already make most of this a compile error. These tests catch the cases a
/// reference cannot: a transitive dependency arriving through a NuGet package, or a host project
/// reaching into a layer it is not supposed to know about.
/// </remarks>
public sealed class LayeringTests
{
    private static readonly Assembly Domain = typeof(ZenithRetail.Domain.AssemblyMarker).Assembly;
    private static readonly Assembly Application = typeof(ZenithRetail.Application.AssemblyMarker).Assembly;
    private static readonly Assembly Contracts = typeof(ZenithRetail.Contracts.AssemblyMarker).Assembly;
    private static readonly Assembly Infrastructure = typeof(ZenithRetail.Infrastructure.AssemblyMarker).Assembly;
    private static readonly Assembly Sync = typeof(ZenithRetail.Sync.AssemblyMarker).Assembly;
    private static readonly Assembly PublicApi = typeof(ZenithRetail.PublicApi.AssemblyMarker).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_in_the_solution()
    {
        Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(
                "ZenithRetail.Application",
                "ZenithRetail.Contracts",
                "ZenithRetail.Infrastructure",
                "ZenithRetail.Sync",
                "ZenithRetail.StoreServer",
                "ZenithRetail.CloudApi",
                "ZenithRetail.PublicApi")
            .GetResult()
            .ShouldPass("Domain references nothing. A compile error here is the correct outcome — "
                + "fix the design, do not add the reference.");
    }

    [Fact]
    public void Domain_depends_on_no_third_party_framework()
    {
        // The point of a dependency-free domain is that business rules can be reasoned about and
        // tested without a database, a web host or a serialiser in the room.
        Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions",
                "Npgsql",
                "Newtonsoft.Json")
            .GetResult()
            .ShouldPass("Domain must stay free of infrastructure and framework dependencies.");
    }

    [Fact]
    public void Application_depends_only_on_Domain()
    {
        Types.InAssembly(Application)
            .Should()
            .NotHaveDependencyOnAny(
                "ZenithRetail.Infrastructure",
                "ZenithRetail.Sync",
                "ZenithRetail.StoreServer",
                "ZenithRetail.CloudApi",
                "ZenithRetail.PublicApi")
            .GetResult()
            .ShouldPass("Application sits above Domain and below Infrastructure. It defines ports; "
                + "Infrastructure implements them.");
    }

    [Fact]
    public void Contracts_stays_dependency_free()
    {
        // Contracts is taken by the desktop app and the Android client. Anything it drags in, they
        // drag in too.
        Types.InAssembly(Contracts)
            .Should()
            .NotHaveDependencyOnAny(
                "ZenithRetail.Domain",
                "ZenithRetail.Application",
                "ZenithRetail.Infrastructure",
                "ZenithRetail.Sync")
            .GetResult()
            .ShouldPass("Contracts are transport DTOs, not a window onto the domain model.");
    }

    [Fact]
    public void Infrastructure_and_Sync_do_not_depend_on_a_host()
    {
        Types.InAssembly(Infrastructure)
            .Should()
            .NotHaveDependencyOnAny("ZenithRetail.StoreServer", "ZenithRetail.CloudApi", "ZenithRetail.PublicApi")
            .GetResult()
            .ShouldPass("Infrastructure is hosted; it does not know its host.");

        Types.InAssembly(Sync)
            .Should()
            .NotHaveDependencyOnAny("ZenithRetail.StoreServer", "ZenithRetail.CloudApi", "ZenithRetail.PublicApi")
            .GetResult()
            .ShouldPass("The same sync protocol runs on the store server and in the cloud.");
    }

    [Fact]
    public void PublicApi_cannot_reach_internal_contracts_or_the_domain_model()
    {
        // CLAUDE.md §7 rule 14 / ADR-021. This host faces the open internet. Cost, margin, supplier and
        // other-customer data must be structurally unreachable — a leak should require deliberately
        // adding a field to a public DTO, not merely forgetting a filter.
        Types.InAssembly(PublicApi)
            .Should()
            .NotHaveDependencyOnAny(
                "ZenithRetail.Contracts",
                "ZenithRetail.Domain",
                "ZenithRetail.Infrastructure")
            .GetResult()
            .ShouldPass("The public API has its own DTOs. Map into them through the Application layer.");
    }
}
