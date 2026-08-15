using System.Reflection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;

namespace VumaRetail.ArchitectureTests;

/// <summary>
/// The two Stage 04b architecture rules the stage brief named and left outstanding.
/// </summary>
/// <remarks>
/// Both close a hole a module author could otherwise open without anyone noticing until a customer
/// hit it: a module that can be granted but never switched off, and a read that could be refused by
/// a lapsed subscription. <c>docs/PROGRESS.md</c> §5 carried these forward from the stage brief.
/// </remarks>
public sealed class LicensingRulesTests
{
    /// <summary>Every assembly that may declare a module's permissions or its manifest.</summary>
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(VumaRetail.Application.AssemblyMarker).Assembly,
        typeof(VumaRetail.Sync.AssemblyMarker).Assembly,
        typeof(VumaRetail.Licensing.AssemblyMarker).Assembly,
        // Stage 07. A rule that only covers the assemblies that existed when it was written stops
        // being a rule — Finance is the first module to live in its own assembly, and it declares
        // both a permission set and a manifest.
        typeof(VumaRetail.Finance.AssemblyMarker).Assembly,
    ];

    /// <summary>Every assembly that may declare a query handler.</summary>
    private static readonly Assembly[] HandlerAssemblies =
    [
        typeof(VumaRetail.Application.AssemblyMarker).Assembly,
        typeof(VumaRetail.Infrastructure.AssemblyMarker).Assembly,
        typeof(VumaRetail.Sync.AssemblyMarker).Assembly,
        typeof(VumaRetail.Licensing.AssemblyMarker).Assembly,
        typeof(VumaRetail.Finance.AssemblyMarker).Assembly,
    ];

    [Fact]
    public void Every_module_that_declares_permissions_also_declares_a_manifest()
    {
        // LICENSING.md §6: a licence gates whole modules. A module with permissions but no manifest is
        // a module a tenant can be granted access to and that nothing can ever switch off — the failure
        // ends with a paid-for module nobody can disable, which is a support call and a refund, not a
        // bug report.
        HashSet<string> withPermissions = ModuleAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => typeof(IModulePermissions).IsAssignableFrom(type))
            .Select(type => ((IModulePermissions)Activator.CreateInstance(type)!).Module)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        HashSet<string> withManifest = ModuleAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => typeof(IModuleManifest).IsAssignableFrom(type))
            .Select(type => ((IModuleManifest)Activator.CreateInstance(type)!).Module)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string> missing = [.. withPermissions.Except(withManifest, StringComparer.OrdinalIgnoreCase)];

        Assert.True(missing.Count == 0, $"""
            A module declares IModulePermissions but no IModuleManifest, so a licence has no flag to
            switch it off with (LICENSING.md §6). Add an IModuleManifest next to the module's
            IModulePermissions declaration, on CoreModuleManifests.cs's or LicensingPermissions.cs's
            pattern.

            Modules missing a manifest: {string.Join(", ", missing)}
            """);
    }

    [Fact]
    public void No_query_handler_depends_on_IEntitlementService()
    {
        // ADR-028 rule 4 / ADR-054: read access is never restricted by licensing, so a query handler
        // has no correct use for the gate. Reporting the enforcement level for display — the licence
        // screen, the sub-lease — goes through IEnforcementStatusReader instead, which can only report
        // and so cannot be used to refuse a read even by accident.
        List<string> offenders = HandlerAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => type.GetInterfaces().Any(i => i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
            .Where(type => type.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(IEntitlementService)))
            .Select(type => type.FullName ?? type.Name)
            .ToList();

        Assert.True(offenders.Count == 0, $"""
            A query handler depends on IEntitlementService. ADR-028 promises reports, reprints and
            exports keep working through a lapse, so a read path must never be able to consult the
            enforcement level to decide whether to answer. Depend on IEnforcementStatusReader instead —
            it can report the level for display but has no method a gate could be built from.

            {string.Join(Environment.NewLine, offenders.Select(name => $"  - {name}"))}
            """);
    }
}
