using VumaRetail.Application.Abstractions.Licensing;

namespace VumaRetail.Application.Loyalty;

/// <summary>The <c>loyalty</c> module manifest (R7).</summary>
public sealed class LoyaltyModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "loyalty";

    /// <inheritdoc />
    public string LicenceFlag => "loyalty";

    /// <inheritdoc />
    public string Description => "Loyalty programme: earn, burn, tiers and rewards.";

    /// <inheritdoc />
    public bool IsCore => false;
}
