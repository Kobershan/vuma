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
