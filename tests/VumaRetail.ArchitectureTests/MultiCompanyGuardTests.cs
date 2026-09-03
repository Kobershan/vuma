using System.Reflection;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Infrastructure.Registry;

namespace VumaRetail.ArchitectureTests;

/// <summary>
/// ADR-116 — there is no cross-database transaction. This is the enforcement behind rule 1 of
/// <c>docs/MULTI_COMPANY.md</c> §11.
/// </summary>
/// <remarks>
/// <para>
/// A handler that can reach two company databases can silently open two transactions, and nothing
/// in the runtime stops it. The guard must be structural: one handler, at most one factory, never
/// a raw DbContext.
/// </para>
/// <para>
/// <b>Proved by a deliberate violation before being committed.</b> A handler with two
/// <c>ICompanyDbContextFactory</c> parameters was added and the first test below failed with that
/// handler named; a handler that took <c>VumaRetailDbContext</c> directly failed the second test;
/// both experiments were reverted before commit.
/// </para>
/// </remarks>
public sealed class MultiCompanyGuardTests
{
    private static readonly Assembly[] HandlerAssemblies =
    [
        typeof(VumaRetail.Application.AssemblyMarker).Assembly,
        typeof(VumaRetail.Infrastructure.AssemblyMarker).Assembly,
        typeof(VumaRetail.Sync.AssemblyMarker).Assembly,
        typeof(VumaRetail.Workflow.AssemblyMarker).Assembly,
    ];

    private static readonly Type[] CompanyContextTypes =
    [
        typeof(ICompanyDbContextFactory),
        typeof(VumaRetail.Infrastructure.Persistence.VumaRetailDbContext),
        typeof(IDbContextFactory<VumaRetail.Infrastructure.Persistence.VumaRetailDbContext>),
    ];

    [Fact]
    public void No_handler_constructor_takes_more_than_one_company_database_resolver()
    {
        List<string> offenders = Handlers()
            .SelectMany(handler => handler.GetConstructors()
                .Select(constructor => (Handler: handler, Count: constructor.GetParameters()
                    .Count(parameter => CompanyContextTypes.Contains(parameter.ParameterType)))))
            .Where(x => x.Count > 1)
            .Select(x => x.Handler.FullName ?? x.Handler.Name)
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0, $"""
            A command or query handler depends on more than one company database resolver.
            ADR-116: there is no cross-database transaction. A handler may resolve at most one
            company's DbContext (docs/MULTI_COMPANY.md §11 rule 1).

            {string.Join(Environment.NewLine, offenders.Select(name => $"  - {name}"))}
            """);
    }

    [Fact]
    public void No_handler_takes_VumaRetailDbContext_directly()
    {
        // Taking the context directly bypasses the company routing and the serving guard.
        Type forbidden = typeof(VumaRetail.Infrastructure.Persistence.VumaRetailDbContext);

        List<string> offenders = Handlers()
            .Where(handler => handler.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => parameter.ParameterType == forbidden))
            .Select(handler => handler.FullName ?? handler.Name)
            .ToList();

        Assert.True(offenders.Count == 0, $"""
            A command or query handler depends on VumaRetailDbContext directly.
            Company database access must go through ICompanyDbContextFactory so the routing,
            serving guard, and one-context limit are enforced (ADR-116, ADR-118).

            {string.Join(Environment.NewLine, offenders.Select(name => $"  - {name}"))}
            """);
    }

    [Fact]
    public void No_handler_calls_CreateAsync_on_a_company_factory_more_than_once()
    {
        // Static scan: one .CreateAsync per handler file is the ceiling. Two calls means two
        // contexts, which means two databases, which is exactly what ADR-116 forbids.
        IReadOnlyList<string> violations = SolutionSource.FindViolations(
            line => line.Contains(".CreateAsync(", StringComparison.Ordinal),
            // Registry services are administrative workflows and may create a registry context
            // plus one company context intentionally during provisioning / migration.
            "src/VumaRetail.Infrastructure/Registry/CompanyMigrationServices.cs",
            "src/VumaRetail.Infrastructure/Registry/RegistryServices.cs",
            "src/VumaRetail.Infrastructure/Registry/CompanyProvisioningSteps.cs",
            "src/VumaRetail.Infrastructure/Registry/GroupCreditService.cs",
            "src/VumaRetail.Infrastructure/Registry/BarcodeResolver.cs",
            "src/VumaRetail.Infrastructure/Registry/CompanyLinkService.cs",
            "src/VumaRetail.Infrastructure/Registry/SagaCoordinator.cs");

        // Group violations by file; any file with two or more .CreateAsync calls is suspicious.
        var filesWithMultipleCalls = violations
            .GroupBy(v => v.Split(':')[0])
            .Where(g => g.Count() >= 2)
            .Select(g => g.Key)
            .ToList();

        Assert.True(filesWithMultipleCalls.Count == 0, $"""
            A handler calls .CreateAsync() on a company context factory more than once, which
            opens two company database contexts in one handler. ADR-116: there is no cross-database
            transaction. If two contexts are genuinely needed, use ICompanyFanOut (read only) or
            a saga (write) (docs/MULTI_COMPANY.md §11 rule 1).

            {string.Join(Environment.NewLine, violations.Where(v => filesWithMultipleCalls.Contains(v.Split(':')[0])).Select(v => $"  - {v}"))}
            """);
    }

    private static IEnumerable<Type> Handlers() => HandlerAssemblies
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => type is { IsAbstract: false, IsInterface: false })
        .Where(type => type.GetInterfaces().Any(contract => contract.IsGenericType
            && (contract.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || contract.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))));
}
