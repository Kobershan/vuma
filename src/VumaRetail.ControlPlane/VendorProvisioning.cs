using System.Security.Cryptography;

namespace VumaRetail.ControlPlane;

public sealed record ProvisionedTenant(string TenantId, string LicenseKey, DateTimeOffset TrialEndsAt,
    DateTimeOffset CreatedAt);

public sealed record OffboardingPackage(string TenantId, DateTimeOffset RequestedAt,
    DateTimeOffset RetainUntil, bool ExportVerified, bool Deleted);

public sealed record EmergencyWriteCode(Guid Id, string TenantId, string Code,
    DateTimeOffset ExpiresAt, bool Redeemed);

public interface IUnlockCodeSigner
{
    Task<string> SignAsync(string tenantId, string nonce, DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
    Task<bool> ValidateAsync(string tenantId, string nonce, string code, DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
}

/// <summary>Vendor-only provisioning and offline unlock policy; no tenant business data access.</summary>
public sealed class VendorProvisioning(IClock? clock = null)
{
    private readonly IClock clock = clock ?? new SystemClockAdapter();
    private readonly Dictionary<string, ProvisionedTenant> tenants = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, EmergencyWriteCode> unlocks = [];
    private readonly Dictionary<string, OffboardingPackage> offboarding = new(StringComparer.Ordinal);

    public ProvisionedTenant Provision(string tenantId, TimeSpan trialLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (trialLength <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(trialLength));
        string normalized = tenantId.Trim();
        if (tenants.ContainsKey(normalized)) throw new InvalidOperationException("Tenant is already provisioned.");
        ProvisionedTenant tenant = new(normalized, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            clock.UtcNow.Add(trialLength), clock.UtcNow);
        tenants.Add(normalized, tenant);
        return tenant;
    }

    public OffboardingPackage RequestOffboarding(string tenantId, bool exportVerified)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (!exportVerified) throw new InvalidOperationException("A verified export is required before offboarding.");
        if (!tenants.ContainsKey(tenantId.Trim())) throw new KeyNotFoundException("Tenant was not found.");
        OffboardingPackage package = new(tenantId.Trim(), clock.UtcNow, clock.UtcNow.AddDays(90), true, false);
        offboarding[package.TenantId] = package;
        return package;
    }

    public bool CanDelete(string tenantId) => offboarding.TryGetValue(tenantId, out OffboardingPackage? package)
        && package.ExportVerified && !package.Deleted && clock.UtcNow >= package.RetainUntil;

    public async Task<EmergencyWriteCode> IssueUnlockAsync(string tenantId, int hours,
        IUnlockCodeSigner signer, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(signer);
        if (hours is < 1 or > 168) throw new ArgumentOutOfRangeException(nameof(hours));
        string normalized = tenantId.Trim();
        if (!tenants.ContainsKey(normalized)) throw new KeyNotFoundException("Tenant was not found.");
        DateTimeOffset expiresAt = clock.UtcNow.AddHours(hours);
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        string code = await signer.SignAsync(normalized, nonce, expiresAt, cancellationToken).ConfigureAwait(false);
        EmergencyWriteCode result = new(Guid.NewGuid(), normalized, $"{nonce}.{code}", expiresAt, false);
        unlocks.Add(result.Id, result);
        return result;
    }

    public async Task<bool> RedeemUnlockAsync(Guid id, string code, IUnlockCodeSigner signer,
        CancellationToken cancellationToken = default)
    {
        if (!unlocks.TryGetValue(id, out EmergencyWriteCode? current) || current.Redeemed || current.ExpiresAt <= clock.UtcNow)
            return false;
        string[] parts = code.Split('.', 2);
        if (parts.Length != 2 || !await signer.ValidateAsync(current.TenantId, parts[0], parts[1], current.ExpiresAt, cancellationToken).ConfigureAwait(false))
            return false;
        unlocks[id] = current with { Redeemed = true };
        return true;
    }
}
