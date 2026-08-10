using System.Reflection;
using VumaRetail.Application.Abstractions;

namespace VumaRetail.ArchitectureTests;

/// <summary>
/// The Stage 03 rule that keeps the transaction boundary in one place (ADR-044).
/// </summary>
/// <remarks>
/// Cheap to obey with eight handlers, and unenforceable by review once there are four hundred. A
/// handler that commits for itself is invisible in a code review and fatal to ADR-006: the outbox
/// row a change produces has to land in the same transaction as the change, and it cannot if the
/// change committed before the outbox behaviour was reached.
/// </remarks>
public sealed class PipelineRulesTests
{
    private static readonly Assembly[] HandlerAssemblies =
    [
        typeof(VumaRetail.Application.AssemblyMarker).Assembly,
        typeof(VumaRetail.Infrastructure.AssemblyMarker).Assembly,
        typeof(VumaRetail.Sync.AssemblyMarker).Assembly,
    ];

    [Fact]
    public void No_message_handler_takes_the_unit_of_work()
    {
        // The pipeline opens the transaction and commits it once (TransactionBehaviour). A handler
        // that wanted IUnitOfWork could only want it to commit, so the dependency is the violation —
        // and it is far easier to spot than the CommitAsync call it would lead to.
        List<string> offenders = Handlers()
            .Where(handler => handler.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(IUnitOfWork)))
            .Select(handler => handler.FullName ?? handler.Name)
            .ToList();

        Assert.True(offenders.Count == 0, $"""
            A command or query handler depends on IUnitOfWork. The Stage 03 pipeline owns the
            transaction: a handler mutates tracked entities and returns, and TransactionBehaviour
            commits once around it (ADR-044, CLAUDE.md §7 rule 2).

            AuthenticationService is the one thing in the system that commits for itself, and it is
            deliberately not a handler (ADR-040).

            {string.Join(Environment.NewLine, offenders.Select(name => $"  - {name}"))}
            """);
    }

    [Fact]
    public void No_handler_calls_CommitAsync()
    {
        // The constructor rule above catches the ordinary case. This one catches a handler that
        // reaches the unit of work some other way — through a repository, or a service locator.
        IReadOnlyList<string> violations = SolutionSource.FindViolations(
            line => line.Contains(".CommitAsync(", StringComparison.Ordinal),
            "src/VumaRetail.Application/Identity/AuthenticationService.cs",
            "src/VumaRetail.Infrastructure/Messaging/PipelineBehaviours.cs",
            "src/VumaRetail.Infrastructure/Persistence/VumaRetailDbContext.cs",
            // The seeder writes the tenant and the store rows directly, because there is no command
            // that creates either until Stage 06. Everything it does that a command covers goes
            // through the dispatcher — which this rule is what forced, the seed having otherwise
            // gone on resolving handlers directly and silently persisting nothing.
            "src/VumaRetail.StoreServer/DemoSeed.cs");

        Assert.True(violations.Count == 0, $"""
            Something outside the pipeline commits the unit of work. The exemptions are
            AuthenticationService (ADR-040 — sign-in is an edge service, not a command), the
            transaction behaviour itself, the DbContext that implements the boundary, and the demo
            seeder's two platform rows.

            {string.Join(Environment.NewLine, violations.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void The_reserved_pipeline_slots_keep_their_order()
    {
        // Stage 04 and Stage 04b each plug one behaviour into a chain forty modules already use.
        // Renumbering these later would silently move the outbox outside the transaction.
        PipelineOrder.Logging.Should().BeLessThan(PipelineOrder.ReadOnlyGuard);
        PipelineOrder.ReadOnlyGuard.Should().BeLessThan(PipelineOrder.Validation);
        PipelineOrder.Validation.Should().BeLessThan(PipelineOrder.Transaction);
        PipelineOrder.Transaction.Should().BeLessThan(PipelineOrder.Outbox);
    }

    private static IEnumerable<Type> Handlers() => HandlerAssemblies
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => type is { IsAbstract: false, IsInterface: false })
        .Where(type => type.GetInterfaces().Any(contract => contract.IsGenericType
            && (contract.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || contract.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))));
}
