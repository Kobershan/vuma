using VumaRetail.Application.Abstractions.Licensing;

namespace VumaRetail.Application.Hr;

/// <summary>The HR module manifest.</summary>
public sealed class HrModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "hr";
    /// <inheritdoc />
    public string LicenceFlag => "hr";
    /// <inheritdoc />
    public string Description => "Employees, employment contracts and leave.";
    /// <inheritdoc />
    public bool IsCore => false;
}

/// <summary>The workforce module manifest.</summary>
public sealed class WorkforceModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "workforce";
    /// <inheritdoc />
    public string LicenceFlag => "workforce";
    /// <inheritdoc />
    public string Description => "Shifts and attendance.";
    /// <inheritdoc />
    public bool IsCore => false;
}
