using System.Reflection;
using VumaRetail.Application.Abstractions.Licensing;

namespace VumaRetail.TestSupport;

/// <summary>
/// Every assembly that may define a command or a query, derived from what the product ships rather
/// than from a list somebody remembered to update (ADR-078).
/// </summary>
/// <remarks>
/// <para>
/// <b>This class exists because the hand-maintained list went stale and nobody noticed for a stage.</b>
/// <c>VumaRetail.Finance</c> shipped 18 commands and 11 queries entirely outside both reflection
/// sweeps (<c>PROGRESS.md</c> §4.16). Nothing failed: <c>ReadOnlyGuardBehaviour</c> passes anything
/// that is not <c>SideEffect.Write</c> straight through, and <c>SideEffect.Unclassified</c> is not
/// <c>Write</c> — so an unattributed command in an unswept assembly would write while a tenant is
/// read-only, which is the exact hole <c>SideEffect.cs</c> says must be unreachable. All 18 commands
/// did carry the attribute, so it was latent rather than live. The next module would have done the
/// same thing.
/// </para>
/// <para>
/// <b>Derivation, not enumeration.</b> The set is every assembly already loaded into the test process
/// whose simple name starts with <c>VumaRetail.</c>, minus the test assemblies themselves. That is
/// broader than the old list and cannot go stale, because an assembly containing commands is by
/// definition referenced by something the tests load.
/// </para>
/// <para>
/// <b>And a belt-and-braces check on top.</b> Loading is lazy in .NET: an assembly that nothing has
/// touched yet is not in <see cref="AppDomain.GetAssemblies"/>. <see cref="All"/> therefore forces
/// every referenced <c>VumaRetail.</c> assembly to load first, walking the reference graph from the
/// test assembly outward. <see cref="ModuleAssembliesTests"/> then asserts that every assembly
/// declaring an <see cref="IModuleManifest"/> is in the result — so a module that somehow escapes the
/// walk fails a test rather than escaping the sweep.
/// </para>
/// </remarks>
public static class ModuleAssemblies
{
    private static readonly Lazy<Assembly[]> Discovered = new(Discover);

    /// <summary>Every product assembly that may define commands or queries.</summary>
    public static Assembly[] All => Discovered.Value;

    /// <summary>The assemblies that declare an <see cref="IModuleManifest"/> — one per sellable module.</summary>
    /// <returns>The assemblies, by name.</returns>
    public static IReadOnlyList<Assembly> WithModuleManifests()
        => [.. All.Where(assembly => assembly.GetTypes().Any(IsModuleManifest))];

    /// <summary>True when a type is a concrete module manifest.</summary>
    /// <param name="type">The type.</param>
    public static bool IsModuleManifest(Type type)
        => type is { IsAbstract: false, IsInterface: false }
            && typeof(IModuleManifest).IsAssignableFrom(type);

    private static Assembly[] Discover()
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        Queue<Assembly> pending = new();

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies().Where(IsProductAssembly))
        {
            pending.Enqueue(assembly);
        }

        List<Assembly> found = [];

        while (pending.Count > 0)
        {
            Assembly assembly = pending.Dequeue();

            if (!seen.Add(assembly.GetName().Name ?? assembly.FullName ?? string.Empty))
            {
                continue;
            }

            found.Add(assembly);

            // Walk outward. Without this, a module assembly no test has yet touched a type from is
            // simply absent from the process, and the sweep silently covers less than it says.
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name?.StartsWith("VumaRetail.", StringComparison.Ordinal) != true
                    || seen.Contains(reference.Name))
                {
                    continue;
                }

                pending.Enqueue(Assembly.Load(reference));
            }
        }

        return [.. found.OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)];
    }

    /// <summary>True for a product assembly the sweeps should cover.</summary>
    /// <param name="assembly">The assembly.</param>
    /// <remarks>
    /// Test assemblies are excluded because they declare commands of their own on purpose — the
    /// read-only carve-out fixtures, for instance — and holding those to the product's rules would
    /// make the rule impossible to test.
    /// </remarks>
    private static bool IsProductAssembly(Assembly assembly)
    {
        string? name = assembly.GetName().Name;

        return name is not null
            && name.StartsWith("VumaRetail.", StringComparison.Ordinal)
            && !name.EndsWith("Tests", StringComparison.Ordinal);
    }
}
