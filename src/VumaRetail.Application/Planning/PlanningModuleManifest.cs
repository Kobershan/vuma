using VumaRetail.Application.Abstractions.Licensing;

namespace VumaRetail.Application.Planning;

/// <summary>The <c>planning</c> module manifest (R7).</summary>
public sealed class PlanningModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "planning";

    /// <inheritdoc />
    public string LicenceFlag => "planning";

    /// <inheritdoc />
    public string Description => "Merchandise planning, forecasting and replenishment.";

    /// <inheritdoc />
    public bool IsCore => false;
}
