using System.Reflection;
using VumaRetail.TestSupport;

namespace VumaRetail.ArchitectureTests;

/// <summary>
/// The test that keeps <see cref="ModuleAssemblies"/> honest — ADR-078's own guard.
/// </summary>
public sealed class ModuleAssembliesTests
{
    [Fact]
    public void Every_module_assembly_is_swept()
    {
        // The two assemblies whose absence caused §4.16 and would cause the next one. Named
        // explicitly on top of the derivation, so that a change to the discovery logic that quietly
        // dropped a module fails here rather than in production two stages later.
        //
        // VumaRetail.Desktop and VumaRetail.Desktop.Gallery are deliberately NOT on this list:
        // this project does not reference them (net9.0-windows cannot build or load on the Linux
        // CI runner, ADR-031), so no reference walk from here can ever reach them on any OS. Their
        // absence from the sweep is safe if and only if they declare no commands — asserted by
        // Desktop_declares_no_commands below, which fails the day someone adds a handler there
        // without wiring the sweep. See docs/PROGRESS.md §4.27.
        string[] mustBePresent =
        [
            "VumaRetail.Application",
            "VumaRetail.Infrastructure",
            "VumaRetail.Finance",
            "VumaRetail.Licensing",
            "VumaRetail.Sync",
            "VumaRetail.Imports",
        ];

        string[] present = [.. ModuleAssemblies.All.Select(assembly => assembly.GetName().Name!)];
        string[] missing = [.. mustBePresent.Except(present, StringComparer.Ordinal)];

        Assert.True(missing.Length == 0, $"""
            These product assemblies are not covered by the reflection sweeps that enforce command
            classification and read-only correctness (ADR-078, PROGRESS.md §4.16). An unattributed
            command in an unswept assembly writes while a tenant is read-only.

            Missing: {string.Join(", ", missing)}
            Swept:   {string.Join(", ", present)}
            """);
    }

    [Fact]
    public void Desktop_declares_no_commands()
    {
        // The companion to Every_module_assembly_is_swept: the Windows-only shells sit outside the
        // sweep (see above), which is only sound while they contain no ICommandHandler. Scanned as
        // source — the assemblies cannot load here — so a handler added to either shell fails this
        // test on every OS rather than slipping past the sweep silently.
        List<string> offenders = [];

        foreach (string project in new[] { "VumaRetail.Desktop", "VumaRetail.Desktop.Gallery" })
        {
            string dir = Path.Combine(SolutionSource.RepositoryRoot.FullName, "src", project);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                if (text.Contains("ICommandHandler<", StringComparison.Ordinal)
                    || text.Contains("IQueryHandler<", StringComparison.Ordinal))
                {
                    offenders.Add(Path.GetRelativePath(SolutionSource.RepositoryRoot.FullName, file));
                }
            }
        }

        Assert.True(offenders.Count == 0, $"""
            A Windows-only shell declares a command or query handler outside every reflection sweep
            (ADR-078). Either move the handler into a swept assembly or wire the shell into the
            sweep with a Windows-runner architecture job.

            {string.Join(Environment.NewLine, offenders.Select(name => $"  - {name}"))}
            """);
    }

    [Fact]
    public void Every_assembly_declaring_a_module_manifest_is_swept()
    {
        // The self-maintaining half. A module is a thing with a manifest; if an assembly has one and
        // is not in the sweep, the sweep is wrong — and this fails without anybody having to remember
        // to add a name to a list.
        IReadOnlyList<Assembly> withManifests = ModuleAssemblies.WithModuleManifests();

        Assert.True(withManifests.Count > 0, """
            No assembly declaring an IModuleManifest was found, which means the discovery walk in
            ModuleAssemblies is not reaching the product assemblies at all. Every other rule that
            depends on it is passing vacuously.
            """);

        Assert.All(withManifests, assembly => Assert.Contains(assembly, ModuleAssemblies.All));
    }
}
