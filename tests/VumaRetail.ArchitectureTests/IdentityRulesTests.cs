using VumaRetail.Application.Catalog.Permissions;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Partners.Permissions;
using VumaRetail.Domain.Identity;
using VumaRetail.Workflow.Permissions;

namespace VumaRetail.ArchitectureTests;

/// <summary>
/// The Stage 02 authorisation rules, enforced now because they become unenforceable later.
/// </summary>
/// <remarks>
/// Both are cheap to obey while there are fourteen permissions and one host, and expensive to
/// retrofit across forty modules. A role-name check added in Stage 19 and found in Stage 29 is not a
/// fix; it is a customer whose renamed role silently stopped authorising anybody.
/// </remarks>
public sealed class IdentityRulesTests
{
    [Fact]
    public void Authorisation_is_by_permission_never_by_role_name()
    {
        // ADR-013. Roles are customer-editable data: a tenant renaming "Manager" to "Duty Manager"
        // must not change what the software does. Permissions are the stable contract, roles are the
        // arrangement of them, and only one of the two belongs in code.
        // The attribute form is matched with its opening quote — `[Authorize(Roles = "Manager")]` —
        // rather than on `Roles =` alone, which also matches the perfectly legitimate `DbSet<Role>
        // Roles => …`. A rule that cries wolf on the model gets suppressed, and then it guards nothing.
        IReadOnlyList<string> violations = SolutionSource.FindViolations(
            line => line.Contains("IsInRole(", StringComparison.Ordinal)
                || line.Contains("RequireRole(", StringComparison.Ordinal)
                || line.Contains("Roles = \"", StringComparison.Ordinal)
                || line.Contains("Roles=\"", StringComparison.Ordinal));

        Assert.True(violations.Count == 0, $"""
            Authorisation names a role rather than a permission (ADR-013, docs/SECURITY.md §2). Use
            RequirePermission("module.entity.action") with a constant from the owning module's
            IModulePermissions declaration.

            {string.Join(Environment.NewLine, violations.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void Every_declared_permission_is_a_well_formed_module_entity_action()
    {
        // The catalogue is assembled at startup from these declarations, and a malformed one throws
        // there — on a customer's server, during a deployment. This is the same check, at build time.
        List<string> offenders = [];

        foreach (IModulePermissions module in DeclaredModules())
        {
            foreach (PermissionDescriptor descriptor in module.Permissions)
            {
                if (!PermissionKey.TryParse(descriptor.Key.Value, out PermissionKey parsed))
                {
                    offenders.Add($"{module.Module}: '{descriptor.Key.Value}' is not module.entity.action");
                    continue;
                }

                if (!string.Equals(parsed.Module, module.Module, StringComparison.Ordinal))
                {
                    offenders.Add($"{module.Module}: '{parsed}' belongs to module '{parsed.Module}'");
                }

                if (string.IsNullOrWhiteSpace(descriptor.Description))
                {
                    offenders.Add($"{module.Module}: '{parsed}' has no description");
                }
            }
        }

        Assert.True(offenders.Count == 0, $"""
            A module's permission declaration is malformed (ADR-013). Permissions are lower-case
            module.entity.action, owned by the module that declares them, and each carries a
            description a shop owner would recognise.

            {string.Join(Environment.NewLine, offenders.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void The_whole_catalogue_assembles_without_a_collision()
    {
        // Two modules claiming one permission is a permission owned by nobody. It only fails at
        // startup otherwise, which means it fails during somebody's deployment window.
        PermissionCatalogue catalogue = new(DeclaredModules());

        Assert.NotEmpty(catalogue.All);
    }

    /// <summary>
    /// Every module that declares permissions. Module stages append theirs here, and the two tests
    /// above then cover them for free.
    /// </summary>
    private static IReadOnlyList<IModulePermissions> DeclaredModules() =>
    [
        new PlatformPermissions(),
        new IdentityPermissions(),
        new WorkflowPermissions(),
        new CatalogPermissions(),
        new PartnerPermissions(),
    ];
}
