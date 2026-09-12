using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Hr;

/// <summary>Permissions for workforce scheduling and attendance.</summary>
public sealed class WorkforcePermissions : IModulePermissions
{
    /// <summary>View shifts and attendance.</summary>
    public const string View = "workforce.shift.view";
    /// <summary>Manage shifts.</summary>
    public const string Manage = "workforce.shift.manage";
    /// <summary>Record attendance.</summary>
    public const string AttendanceRecord = "workforce.attendance.record";

    /// <inheritdoc />
    public string Module => "workforce";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(View), "View workforce shifts and attendance."),
        new(PermissionKey.Parse(Manage), "Create workforce shifts.", IsHighRisk: true),
        new(PermissionKey.Parse(AttendanceRecord), "Record workforce attendance.", IsHighRisk: true),
    ];
}
