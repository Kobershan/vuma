using System.Reflection;
using ZenithRetail.Application.Abstractions;

namespace ZenithRetail.ArchitectureTests;

/// <summary>
/// CLAUDE.md §7 rule 15 — every command declares whether it writes.
/// </summary>
/// <remarks>
/// <para>
/// This is the test that stops read-only enforcement (ADR-028) from developing quiet holes. Stage 04b
/// refuses writes with a single pipeline interceptor; the interceptor can only do that if every
/// command says what it is. A module author who forgets the attribute finds out here, at build time,
/// rather than a customer finding out by ringing up a sale during a lapse.
/// </para>
/// <para>
/// There are no commands yet. These tests are written before there is anything to check on purpose —
/// the first command anyone adds is already governed.
/// </para>
/// </remarks>
public sealed class CommandClassificationTests
{
    /// <summary>
    /// Every assembly that may define commands. Module assemblies get appended here as stages add them.
    /// </summary>
    private static readonly Assembly[] CommandAssemblies =
    [
        typeof(ZenithRetail.Application.AssemblyMarker).Assembly,
        typeof(ZenithRetail.Infrastructure.AssemblyMarker).Assembly,
        typeof(ZenithRetail.Sync.AssemblyMarker).Assembly,
    ];

    [Fact]
    public void Every_command_declares_a_side_effect()
    {
        List<string> unclassified = [];

        foreach (Type command in AllCommands())
        {
            CommandSideEffectAttribute? attribute = command.GetCustomAttribute<CommandSideEffectAttribute>();

            if (attribute is null)
            {
                unclassified.Add($"{command.FullName} — no [CommandSideEffect] attribute");
            }
            else if (attribute.Effect == SideEffect.Unclassified)
            {
                unclassified.Add($"{command.FullName} — declared SideEffect.Unclassified");
            }
        }

        Assert.True(unclassified.Count == 0, $"""
            Every ICommand must declare whether it writes, so the Stage 04b read-only interceptor can
            refuse writes without each module checking for itself (CLAUDE.md §7 rule 15, ADR-028).

            Add [CommandSideEffect(SideEffect.Write)] or [CommandSideEffect(SideEffect.ReadOnly)] to:
            {string.Join(Environment.NewLine, unclassified.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void Read_only_exemptions_stay_confined_to_the_three_ADR_028_carve_outs()
    {
        // Each of these is a hole in the commercial lever. ADR-028 names exactly three and the set is
        // meant to stay reviewable in one place — a fourth should cost an ADR, not a code review.
        List<string> exempt = AllCommands()
            .Select(command => (Type: command, Attribute: command.GetCustomAttribute<CommandSideEffectAttribute>()))
            .Where(entry => entry.Attribute is { Exemption: not ReadOnlyExemption.None })
            .Select(entry => $"{entry.Type.FullName} — {entry.Attribute!.Exemption}")
            .ToList();

        Assert.True(exempt.Count <= 3, $"""
            More commands claim a read-only exemption than ADR-028 allows. The permitted carve-outs are
            Payment, OfflineFlush and Backup. Widening the set needs a superseding ADR.

            Currently exempt:
            {string.Join(Environment.NewLine, exempt.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void Queries_are_never_marked_as_writing()
    {
        List<string> offenders = CommandAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => type.GetInterfaces().Any(i => i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IQuery<>)))
            .Where(type => type.GetCustomAttribute<CommandSideEffectAttribute>()?.Effect == SideEffect.Write)
            .Select(type => type.FullName ?? type.Name)
            .ToList();

        Assert.True(offenders.Count == 0, $"""
            A query marked as a write would be refused while a tenant is read-only, which breaks the
            ADR-028 guarantee that reporting, reprints and exports keep working. If it really writes,
            it is a command.

            {string.Join(Environment.NewLine, offenders.Select(name => $"  - {name}"))}
            """);
    }

    private static IEnumerable<Type> AllCommands() => CommandAssemblies
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => type is { IsAbstract: false, IsInterface: false })
        .Where(type => type.GetInterfaces().Any(i => i.IsGenericType
            && i.GetGenericTypeDefinition() == typeof(ICommand<>)));
}
