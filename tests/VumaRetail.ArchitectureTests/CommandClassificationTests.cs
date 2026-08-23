using System.Reflection;
using VumaRetail.Application.Abstractions;
using VumaRetail.TestSupport;

namespace VumaRetail.ArchitectureTests;

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
/// Written in Stage 00, before there was a single command to check, so that the first command anybody
/// added was already governed. The assembly list it sweeps became derived rather than hand-maintained
/// in Stage 11, after the hand-maintained one went stale by a whole module (ADR-078).
/// </para>
/// </remarks>
public sealed class CommandClassificationTests
{
    /// <summary>
    /// Every assembly that may define commands.
    /// </summary>
    /// <remarks>
    /// Derived rather than listed, since Stage 11 (ADR-078). It used to be a hand-maintained array,
    /// and it went stale: <c>VumaRetail.Finance</c> shipped 18 commands entirely outside this sweep
    /// for a whole stage (<c>PROGRESS.md</c> §4.16), which meant an unattributed command there would
    /// have written while a tenant was read-only. See <see cref="ModuleAssemblies"/>.
    /// </remarks>
    private static Assembly[] CommandAssemblies => ModuleAssemblies.All;

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
    public void Read_only_exemptions_stay_confined_to_the_four_ADR_028_ADR_135_carve_outs()
    {
        // The cap is on *kinds*, not commands (ADR-052) — counting commands would fail the build the
        // moment a second command legitimately shared a kind that already had one user, which is
        // exactly what Stage 04b's licence-screen commands do under Payment (SideEffect.cs's own
        // remarks on ReadOnlyExemption.Payment name six of them by design). What must stay closed is
        // the set of *kinds*: Payment, OfflineFlush, Backup and, since ADR-135, ReceiptReprint —
        // asserted by name so a fifth needs a superseding ADR rather than a code review.
        List<(Type Type, ReadOnlyExemption Exemption)> exempt = AllCommands()
            .Select(command => (Type: command, Attribute: command.GetCustomAttribute<CommandSideEffectAttribute>()))
            .Where(entry => entry.Attribute is { Exemption: not ReadOnlyExemption.None })
            .Select(entry => (entry.Type, entry.Attribute!.Exemption))
            .ToList();

        HashSet<ReadOnlyExemption> kinds = [.. exempt.Select(entry => entry.Exemption)];

        HashSet<ReadOnlyExemption> permitted =
        [
            ReadOnlyExemption.Payment,
            ReadOnlyExemption.OfflineFlush,
            ReadOnlyExemption.Backup,
            ReadOnlyExemption.ReceiptReprint,
        ];

        Assert.True(kinds.IsSubsetOf(permitted) && kinds.Count <= 4, $"""
            A read-only exemption kind outside the closed list of four (Payment, OfflineFlush, Backup,
            ReceiptReprint) is in use. Widening the set of kinds needs a superseding ADR.

            Currently exempt:
            {string.Join(Environment.NewLine, exempt.Select(entry => $"  - {entry.Type.FullName} — {entry.Exemption}"))}
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
