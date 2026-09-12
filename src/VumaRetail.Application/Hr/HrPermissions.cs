using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Hr;

/// <summary>Permissions for employee, contract and leave administration.</summary>
public sealed class HrPermissions : IModulePermissions
{
    /// <summary>View employees and contracts.</summary>
    public const string View = "hr.employee.view";
    /// <summary>Manage employees and contracts.</summary>
    public const string Manage = "hr.employee.manage";
    /// <summary>View leave requests.</summary>
    public const string LeaveView = "hr.leave.view";
    /// <summary>Manage leave requests.</summary>
    public const string LeaveManage = "hr.leave.manage";

    /// <inheritdoc />
    public string Module => "hr";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(View), "View employees and employment contracts."),
        new(PermissionKey.Parse(Manage), "Create employees and employment contracts.", IsHighRisk: true),
        new(PermissionKey.Parse(LeaveView), "View employee leave requests."),
        new(PermissionKey.Parse(LeaveManage), "Create and decide employee leave requests.", IsHighRisk: true),
    ];
}
