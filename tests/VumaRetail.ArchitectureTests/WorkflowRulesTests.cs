using System.Reflection;
using VumaRetail.Application.Abstractions.Workflow;

namespace VumaRetail.ArchitectureTests;

/// <summary>
/// The Stage 05 rule <c>CLAUDE.md</c> §7 rule 13 states in words: no module implements its own
/// approval logic, everything gates through <see cref="IApprovalService"/>.
/// </summary>
/// <remarks>
/// Proven by a deliberate violation before being committed, the same way every other enforced
/// architecture rule in this repository is: a second, throwaway <c>IApprovalService</c> was added to
/// <c>VumaRetail.Infrastructure</c>, this test was confirmed red, and the throwaway type was then
/// removed and the test confirmed green again. See <c>docs/PROGRESS.md</c>'s Stage 05 session log for
/// the transcript of that run.
/// </remarks>
public sealed class WorkflowRulesTests
{
    [Fact]
    public void Only_VumaRetail_Workflow_implements_IApprovalService()
    {
        List<string> offenders = [];

        foreach (Assembly assembly in RelevantAssemblies())
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (type is { IsAbstract: false, IsInterface: false }
                    && typeof(IApprovalService).IsAssignableFrom(type)
                    && !string.Equals(assembly.GetName().Name, "VumaRetail.Workflow", StringComparison.Ordinal))
                {
                    offenders.Add($"{type.FullName} (in {assembly.GetName().Name})");
                }
            }
        }

        Assert.True(offenders.Count == 0, $"""
            IApprovalService has an implementation outside VumaRetail.Workflow (ADR-019, CLAUDE.md §7
            rule 13). ApprovalEngine is the single choke point every module gates through; a second
            implementation is exactly the "ten modules, ten inconsistent approval chains" problem
            Stage 05 exists to prevent.

            {string.Join(Environment.NewLine, offenders.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void Exactly_one_implementation_of_IApprovalService_exists_at_all()
    {
        // The companion half of the rule above: not merely "nowhere else", but "somewhere, exactly
        // once" — a solution with zero implementations would pass the first test vacuously.
        List<Type> implementations = RelevantAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => typeof(IApprovalService).IsAssignableFrom(type))
            .ToList();

        Assert.True(implementations.Count == 1, $"""
            Expected exactly one implementation of IApprovalService, found {implementations.Count}:
            {string.Join(Environment.NewLine, implementations.Select(type => $"  - {type.FullName}"))}
            """);
    }

    private static IEnumerable<Assembly> RelevantAssemblies() =>
    [
        typeof(VumaRetail.Application.AssemblyMarker).Assembly,
        typeof(VumaRetail.Infrastructure.AssemblyMarker).Assembly,
        typeof(VumaRetail.Sync.AssemblyMarker).Assembly,
        typeof(VumaRetail.Licensing.AssemblyMarker).Assembly,
        typeof(VumaRetail.Workflow.AssemblyMarker).Assembly,
        typeof(VumaRetail.Web.VumaWebExtensions).Assembly,
    ];
}
