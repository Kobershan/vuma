using VumaRetail.Application.Abstractions.Licensing;

namespace VumaRetail.Application.Manufacturing;

/// <summary>The Stage 16 manufacturing module manifest.</summary>
public sealed class ManufacturingModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "manufacturing";

    /// <inheritdoc />
    public string LicenceFlag => "manufacturing";

    /// <inheritdoc />
    public string Description => "Bill of materials, alternates, routings and rolled-up costing.";

    /// <inheritdoc />
    public bool IsCore => false;
}
