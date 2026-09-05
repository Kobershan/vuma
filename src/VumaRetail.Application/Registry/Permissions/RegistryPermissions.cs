using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Registry;

/// <summary>What the <c>registry</c> (trading group) module lets somebody do.</summary>
public sealed class RegistryPermissions : IModulePermissions
{
    /// <summary>View company links.</summary>
    public const string GroupLinkView = "registry.grouplink.view";
    /// <summary>Propose a company link.</summary>
    public const string GroupLinkPropose = "registry.grouplink.propose";
    /// <summary>Accept a company link.</summary>
    public const string GroupLinkAccept = "registry.grouplink.accept";
    /// <summary>Revoke a company link.</summary>
    public const string GroupLinkRevoke = "registry.grouplink.revoke";
    /// <summary>Manage premises.</summary>
    public const string PremisesManage = "registry.premises.manage";
    /// <summary>Manage registry users.</summary>
    public const string RegistryUserManage = "registry.user.manage";
    /// <summary>Manage terminals.</summary>
    public const string TerminalManage = "registry.terminal.manage";

    // Stage 07c: Cross-company money
    /// <summary>Capture a group receipt.</summary>
    public const string GroupReceiptCapture = "group.receipt.capture";
    /// <summary>Allocate a group receipt to a company.</summary>
    public const string GroupReceiptAllocate = "group.receipt.allocate";
    /// <summary>Reverse a group receipt.</summary>
    public const string GroupReceiptReverse = "group.receipt.reverse";
    /// <summary>Capture a group payment run.</summary>
    public const string GroupPaymentCapture = "group.payment.capture";
    /// <summary>Allocate a group payment run to a company.</summary>
    public const string GroupPaymentAllocate = "group.payment.allocate";
    /// <summary>View consolidated reports.</summary>
    public const string GroupReportConsolidated = "group.report.consolidated";

    /// <inheritdoc />
    public string Module => "registry";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(GroupLinkView), "View company links."),
        new(PermissionKey.Parse(GroupLinkPropose), "Propose a company link.", IsHighRisk: true),
        new(PermissionKey.Parse(GroupLinkAccept), "Accept a company link.", IsHighRisk: true),
        new(PermissionKey.Parse(GroupLinkRevoke), "Revoke a company link.", IsHighRisk: true),
        new(PermissionKey.Parse(PremisesManage), "Manage premises."),
        new(PermissionKey.Parse(RegistryUserManage), "Manage registry users.", IsHighRisk: true),
        new(PermissionKey.Parse(TerminalManage), "Manage terminals.", IsHighRisk: true),
        new(PermissionKey.Parse(GroupReceiptCapture), "Capture a group receipt."),
        new(PermissionKey.Parse(GroupReceiptAllocate), "Allocate a group receipt to a company."),
        new(PermissionKey.Parse(GroupReceiptReverse), "Reverse a group receipt.", IsHighRisk: true),
        new(PermissionKey.Parse(GroupPaymentCapture), "Capture a group payment run."),
        new(PermissionKey.Parse(GroupPaymentAllocate), "Allocate a group payment run to a company."),
        new(PermissionKey.Parse(GroupReportConsolidated), "View consolidated reports."),
    ];
}

/// <summary>
/// The <c>registry</c> (trading group) module's manifest (R7).
/// </summary>
/// <remarks>
/// Core. Company links, premises, the user directory and terminals are the substrate every
/// cross-company operation stands on — a licence that omitted them would describe companies
/// that can never cooperate, with no way back in to fix it.
/// </remarks>
public sealed class RegistryModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "registry";

    /// <inheritdoc />
    public string LicenceFlag => "registry";

    /// <inheritdoc />
    public string Description => "Operator ID, company links, shared premises, cross-company users and tills.";

    /// <inheritdoc />
    public bool IsCore => true;
}
