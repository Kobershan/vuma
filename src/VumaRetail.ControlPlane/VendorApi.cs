using System.Security.Cryptography.X509Certificates;

namespace VumaRetail.ControlPlane;

public sealed record StartRolloutRequest(string Version, string Channel, int Percentage);
public sealed record ProvisionTenantRequest(string TenantId, int TrialDays);
public sealed record ObserveAbuseRequest(string LicenseKey, string InstallFingerprint, string NodeId,
    long MonotonicCounter, DateTimeOffset At, double Latitude, double Longitude, string DocumentSeries);
public sealed record SupportGrantRequest(string TenantId, string RequestedBy, DateTimeOffset ExpiresAt,
    bool TenantApproved);
public sealed record BillingCalculationRequest(BillingPlan Plan, BillingUsage Usage);

public sealed class VendorApiAuthorizationOptions
{
    public bool AllowDevelopmentHeader { get; set; } = true;
    public Dictionary<string, VendorRole> CertificateRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Minimal vendor identity boundary for the separately deployed control plane.</summary>
public static class VendorApiAuthorization
{
    public static bool IsAllowed(HttpContext context, VendorRole minimumRole)
        => IsAllowed(context, minimumRole, new VendorApiAuthorizationOptions(), isDevelopment: true);

    public static bool IsAllowed(HttpContext context, VendorRole minimumRole,
        VendorApiAuthorizationOptions options, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        X509Certificate2? certificate = context.Connection.ClientCertificate;
        if (certificate is not null)
        {
            string thumbprint = NormalizeThumbprint(certificate.Thumbprint);
            if (options.CertificateRoles.TryGetValue(thumbprint, out VendorRole certificateRole))
            {
                return HasRole(certificateRole, minimumRole);
            }
        }

        // A caller-controlled header is useful for local development only. It must never be an
        // authorization mechanism in production, even when mutual TLS is enabled: possession of a
        // valid client certificate is not proof that the caller has an administrator role.
        if (!isDevelopment || !options.AllowDevelopmentHeader)
        {
            return false;
        }

        string? rawRole = context.Request.Headers["X-Vendor-Role"].FirstOrDefault();
        if (!Enum.TryParse(rawRole, true, out VendorRole role))
        {
            return false;
        }

        return HasRole(role, minimumRole);
    }

    private static bool HasRole(VendorRole actual, VendorRole minimum)
        => actual == VendorRole.Admin || actual == minimum;

    private static string NormalizeThumbprint(string? thumbprint)
        => new string((thumbprint ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
