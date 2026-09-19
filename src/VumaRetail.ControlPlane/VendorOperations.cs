namespace VumaRetail.ControlPlane;

public sealed record DeviceObservation(string LicenseKey, string InstallFingerprint, string NodeId,
    long MonotonicCounter, DateTimeOffset At, double Latitude, double Longitude, string DocumentSeries);

public sealed record AbuseSignal(string Code, string NodeId, string Evidence);

public sealed record AbuseCase(Guid Id, AbuseSignal Signal, string? Verdict, string? Action);

/// <summary>Detects suspicious device observations without taking enforcement action.</summary>
public sealed class AbuseDetector
{
    private readonly Dictionary<string, DeviceObservation> _lastByNode = [];
    private readonly List<AbuseCase> _cases = [];

    public IReadOnlyList<AbuseCase> Cases => _cases;

    public IReadOnlyList<AbuseCase> Observe(DeviceObservation observation)
    {
        List<AbuseCase> found = [];
        foreach (DeviceObservation previous in _lastByNode.Values.Where(x => x.LicenseKey == observation.LicenseKey))
        {
            if (previous.InstallFingerprint != observation.InstallFingerprint)
            {
                found.Add(New("duplicate-install", observation, $"fingerprint:{previous.InstallFingerprint}"));
            }

            if (previous.DocumentSeries == observation.DocumentSeries && previous.NodeId != observation.NodeId)
            {
                found.Add(New("colliding-document-series", observation, $"series:{observation.DocumentSeries}"));
            }
        }
        if (_lastByNode.TryGetValue(observation.NodeId, out DeviceObservation? last))
        {
            if (observation.MonotonicCounter < last.MonotonicCounter)
            {
                found.Add(New("counter-rollback", observation, $"previous:{last.MonotonicCounter}"));
            }

            if (observation.At < last.At)
            {
                found.Add(New("clock-rollback", observation, $"previous:{last.At:O}"));
            }

            if (DistanceKm(last.Latitude, last.Longitude, observation.Latitude, observation.Longitude)
                > 900 && (observation.At - last.At).TotalHours < 2)
            {
                found.Add(New("impossible-travel", observation, $"distance-km:{DistanceKm(last.Latitude, last.Longitude, observation.Latitude, observation.Longitude):F0}"));
            }
        }
        _lastByNode[observation.NodeId] = observation;
        _cases.AddRange(found);
        return found;
    }

    public AbuseCase Resolve(Guid caseId, string verdict, string action)
    {
        AbuseCase current = _cases.Single(x => x.Id == caseId);
        AbuseCase resolved = current with { Verdict = verdict, Action = action };
        _cases[_cases.IndexOf(current)] = resolved;
        return resolved;
    }

    private static AbuseCase New(string code, DeviceObservation observation, string evidence)
        => new(Guid.NewGuid(), new AbuseSignal(code, observation.NodeId, evidence), null, null);

    private static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        double radians = Math.PI / 180;
        double a = Math.Pow(Math.Sin((lat2 - lat1) * radians / 2), 2) + Math.Cos(lat1 * radians)
            * Math.Cos(lat2 * radians) * Math.Pow(Math.Sin((lon2 - lon1) * radians / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(a));
    }
}

public enum VendorRole { Support, Billing, Engineering, Admin }

public sealed record VendorPrincipal(string Id, VendorRole Role, string? PartnerId);

public sealed class VendorScope
{
    public static bool CanViewTenant(VendorPrincipal principal, string tenantId, IReadOnlyDictionary<string, string> tenantPartners)
        => principal.Role == VendorRole.Admin || principal.PartnerId is null
            || tenantPartners.TryGetValue(tenantId, out string? partner) && partner == principal.PartnerId;
}

public sealed record SupportGrant(Guid Id, string TenantId, string RequestedBy, DateTimeOffset ExpiresAt,
    bool TenantApproved, bool Active);

public static class SupportGrantPolicy
{
    public static SupportGrant Create(Guid id, string tenantId, string requestedBy, DateTimeOffset expiresAt,
        bool tenantApproved, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (!tenantApproved || expiresAt <= now)
        {
            throw new InvalidOperationException("Tenant approval and a future expiry are required.");
        }

        return new SupportGrant(id, tenantId, requestedBy, expiresAt, true, true);
    }

    public static bool IsActive(SupportGrant grant, DateTimeOffset now) => grant.Active && grant.TenantApproved && grant.ExpiresAt > now;
}
