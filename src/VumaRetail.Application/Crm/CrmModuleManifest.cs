using VumaRetail.Application.Abstractions.Licensing;

namespace VumaRetail.Application.Crm;

/// <summary>The <c>crm</c> module manifest (R7).</summary>
public sealed class CrmModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "crm";

    /// <inheritdoc />
    public string LicenceFlag => "crm";

    /// <inheritdoc />
    public string Description => "Customer relationships: leads, opportunities, activities, segments and consent.";

    /// <inheritdoc />
    public bool IsCore => false;
}
