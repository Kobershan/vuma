using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Registry;

/// <summary>What the <c>registry</c> (trading group) module lets somebody do.</summary>
public sealed class RegistryPermissions : IModulePermissions
{
    /// <summary>View company links.</summary>
    public const string GroupLinkView = "grouplink.view";
    /// <summary>Propose a company link.</summary>
    public const string GroupLinkPropose = "grouplink.propose";
    /// <summary>Accept a company link.</summary>
    public const string GroupLinkAccept = "grouplink.accept";
    /// <summary>Revoke a company link.</summary>
    public const string GroupLinkRevoke = "grouplink.revoke";
    /// <summary>Manage premises.</summary>
    public const string PremisesManage = "premises.manage";
    /// <summary>Manage registry users.</summary>
    public const string RegistryUserManage = "registryuser.manage";
    /// <summary>Manage terminals.</summary>
    public const string TerminalManage = "terminal.manage";

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
    ];
}
