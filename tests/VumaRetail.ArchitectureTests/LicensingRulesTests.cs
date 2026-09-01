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

    /// <summary>Every module assembly, excluding <c>VumaRetail.Licensing</c> itself — that one is licensing.</summary>
    private static readonly Assembly[] BusinessModuleAssemblies =
    [
        typeof(VumaRetail.Application.AssemblyMarker).Assembly,
        typeof(VumaRetail.Sync.AssemblyMarker).Assembly,
        typeof(VumaRetail.Finance.AssemblyMarker).Assembly,
    ];

    /// <summary>
    /// The licensing-internal types only <c>VumaRetail.Licensing</c> may depend on directly.
    /// <see cref="IEntitlementService"/> is deliberately not here — <c>CheckLimitAsync</c> is the
    /// sanctioned choke point a handler is expected to call for a seat/device/store limit, exactly the
    /// "nothing else" RequireModule's doc comment carves out. What this rule forbids is bypassing that
    /// choke point to read the raw lease/activation state or the enforcement level directly.
    /// </summary>
    private static readonly Type[] LicensingInternals =
    [
        typeof(IEnforcementStatusReader),
        typeof(IActivationRepository),
        typeof(ILeaseRepository),
        typeof(ILicenceStateRepository),
    ];

    /// <summary>
    /// The one handler outside <c>VumaRetail.Licensing</c> sanctioned to depend on
    /// <see cref="IEnforcementStatusReader"/> directly, and the single type it may depend on (§4.10,
    /// ADR-135).
    /// </summary>
    /// <remarks>
    /// <c>RecordReceiptPrintCommand</c> is exempt from the read-only guard itself
    /// (<c>ReadOnlyExemption.ReceiptReprint</c>) on the "cannot originate trade" argument, which only
    /// holds for a sale that already exists in full — so the handler is the one place left that can
    /// still ask whether the tenant is read-only, to refuse the case the exemption does not cover: a
    /// first print of a sale that is still <c>Open</c>. This is not a second "are we allowed to" for
    /// something <c>RequireModule</c>/<c>CheckLimitAsync</c> already answer — it is the bound a command
    /// exempted from the pipeline's own check has to enforce for itself. A closed, named list rather
    /// than a blanket carve-out: a handler earns its way onto this list one entry at a time, the same
    /// shape as <c>ReadOnlyExemption</c>'s own cap.
    /// </remarks>
    private static readonly (Type Handler, Type Dependency)[] SanctionedEnforcementStatusReaders =
    [
        (typeof(VumaRetail.Application.Pos.Commands.RecordReceiptPrintCommandHandler),
            typeof(IEnforcementStatusReader)),
    ];

    [Fact]
    public void No_command_or_query_handler_outside_licensing_reads_enforcement_state_directly()
    {
        // RequireModule's own doc comment (LicensingEndpoints.cs) promises this: "it goes through
        // IEntitlementService and nothing else — an architecture test fails the build on a module that
        // reads a licence or an enforcement level for itself, because a second implementation of 'are
        // we allowed to' is a second set of edge cases and only one of them gets fixed." No test backed
        // that promise until now — found by architecture-guard against commits be301ae..8d4adcb, which
        // wired RequireModule into five modules on the strength of that unenforced guarantee.
        //
        // VumaRetail.Licensing itself is exempt — its own command/query handlers (activation, heartbeat,
        // the licence screen) are licensing, not a module reading it for itself.
        List<string> offenders = BusinessModuleAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => type.GetInterfaces().Any(i => i.IsGenericType
                && (i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                    || i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))))
            .Where(type => type.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .Any(dependency => LicensingInternals.Contains(dependency)
                    && !SanctionedEnforcementStatusReaders.Contains((type, dependency))))
            .Select(type => type.FullName ?? type.Name)
            .ToList();

        Assert.True(offenders.Count == 0, $"""
            A command or query handler outside VumaRetail.Licensing depends on a licensing-internal type
            directly (IEnforcementStatusReader, IActivationRepository, ILeaseRepository or
            ILicenceStateRepository) rather than going through IEntitlementService, the sanctioned choke
            point. RequireModule's own doc comment promises this never happens — "are we allowed to" has
            exactly one implementation. A handler that needs a seat/device/store limit calls
            IEntitlementService.CheckLimitAsync; a module that needs to know whether it is entitled
            belongs behind RequireModule at the endpoint; a screen that needs to display enforcement
            state depends on IEnforcementStatusReader only through the licensing module's own query
            handlers, not by reading it for itself.

            {string.Join(Environment.NewLine, offenders.Select(name => $"  - {name}"))}
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
