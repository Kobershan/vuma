namespace VumaRetail.ControlPlane;

public sealed record StartRolloutRequest(string Version, string Channel, int Percentage);
public sealed record ProvisionTenantRequest(string TenantId, int TrialDays);
public sealed record ObserveAbuseRequest(string LicenseKey, string InstallFingerprint, string NodeId,
    long MonotonicCounter, DateTimeOffset At, double Latitude, double Longitude, string DocumentSeries);
public sealed record SupportGrantRequest(string TenantId, string RequestedBy, DateTimeOffset ExpiresAt,
    bool TenantApproved);
public sealed record BillingCalculationRequest(BillingPlan Plan, BillingUsage Usage);

/// <summary>Minimal vendor identity boundary for the separately deployed control plane.</summary>
public static class VendorApiAuthorization
{
    public static bool IsAllowed(HttpContext context, VendorRole minimumRole)
    {
        ArgumentNullException.ThrowIfNull(context);
        string? rawRole = context.Request.Headers["X-Vendor-Role"].FirstOrDefault();
        if (!Enum.TryParse(rawRole, true, out VendorRole role))
        {
            return false;
        }

        return role == VendorRole.Admin || role == minimumRole;
    }
}
