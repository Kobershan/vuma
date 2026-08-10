using System.Reflection;
using NetArchTest.Rules;

namespace VumaRetail.ArchitectureTests;

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
    private static readonly Assembly Domain = typeof(VumaRetail.Domain.AssemblyMarker).Assembly;
    private static readonly Assembly Application = typeof(VumaRetail.Application.AssemblyMarker).Assembly;
    private static readonly Assembly Contracts = typeof(VumaRetail.Contracts.AssemblyMarker).Assembly;
    private static readonly Assembly Infrastructure = typeof(VumaRetail.Infrastructure.AssemblyMarker).Assembly;
    private static readonly Assembly Sync = typeof(VumaRetail.Sync.AssemblyMarker).Assembly;
    private static readonly Assembly PublicApi = typeof(VumaRetail.PublicApi.AssemblyMarker).Assembly;
    private static readonly Assembly Web = typeof(VumaRetail.Web.VumaWebExtensions).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_in_the_solution()
    {
        Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(
                "VumaRetail.Application",
                "VumaRetail.Contracts",
                "VumaRetail.Infrastructure",
                "VumaRetail.Sync",
                "VumaRetail.StoreServer",
                "VumaRetail.CloudApi",
                "VumaRetail.PublicApi")
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
                "VumaRetail.Infrastructure",
                "VumaRetail.Sync",
                "VumaRetail.StoreServer",
                "VumaRetail.CloudApi",
                "VumaRetail.PublicApi")
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
                "VumaRetail.Domain",
                "VumaRetail.Application",
                "VumaRetail.Infrastructure",
                "VumaRetail.Sync")
            .GetResult()
            .ShouldPass("Contracts are transport DTOs, not a window onto the domain model.");
    }

    [Fact]
    public void Infrastructure_and_Sync_do_not_depend_on_a_host()
    {
        Types.InAssembly(Infrastructure)
            .Should()
            .NotHaveDependencyOnAny("VumaRetail.StoreServer", "VumaRetail.CloudApi", "VumaRetail.PublicApi")
            .GetResult()
            .ShouldPass("Infrastructure is hosted; it does not know its host.");

        Types.InAssembly(Sync)
            .Should()
            .NotHaveDependencyOnAny("VumaRetail.StoreServer", "VumaRetail.CloudApi", "VumaRetail.PublicApi")
            .GetResult()
            .ShouldPass("The same sync protocol runs on the store server and in the cloud.");
    }

    [Fact]
    public void Web_is_hosted_and_does_not_know_its_host()
    {
        // VumaRetail.Web holds the ASP.NET Core wiring the StoreServer and the CloudApi share
        // (ADR-039). It sits above Infrastructure and below the hosts; a reference to either host
        // would make it that host's code with extra steps.
        Types.InAssembly(Web)
            .Should()
            .NotHaveDependencyOnAny("VumaRetail.StoreServer", "VumaRetail.CloudApi", "VumaRetail.PublicApi")
            .GetResult()
            .ShouldPass("The same web wiring runs in the store server and in the cloud.");
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
                "VumaRetail.Contracts",
                "VumaRetail.Domain",
                "VumaRetail.Infrastructure",
                "VumaRetail.Web")
            .GetResult()
            .ShouldPass("The public API has its own DTOs. Map into them through the Application layer.");
    }
}
