#pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Connect;

/// <summary>A supplier-issued, expiring invitation that never grants data access by itself.</summary>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class ConnectionCode : Entity
{
    private ConnectionCode(Guid tenantId, string code, int uses, DateTimeOffset expiresAt, string? priceTier,
        string? territory, bool grantsPortalAccess) : base(tenantId)
    {
        Code = code;
        MaxUses = uses;
        ExpiresAt = expiresAt;
        PriceTier = priceTier;
        Territory = territory;
        GrantsPortalAccess = grantsPortalAccess;
    }

    private ConnectionCode() { }

    public string Code { get; private set; } = string.Empty;
    public int MaxUses { get; private set; }
    public int RedeemedUses { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public string? PriceTier { get; private set; }
    public string? Territory { get; private set; }
    public bool GrantsPortalAccess { get; private set; }
    public bool IsRedeemable(DateTimeOffset at) => at < ExpiresAt && RedeemedUses < MaxUses && DeletedAt is null;

    public static ConnectionCode Issue(Guid supplierTenantId, string code, int uses, DateTimeOffset expiresAt,
        string? priceTier, string? territory, bool grantsPortalAccess, DateTimeOffset now)
    {
        if (supplierTenantId == Guid.Empty) throw new ArgumentException("Supplier tenant is required.");
        if (string.IsNullOrWhiteSpace(code) || uses < 1 || expiresAt <= now) throw new ArgumentException("Invalid connection code.");
        return new ConnectionCode(supplierTenantId, code.Trim().ToUpperInvariant(), uses, expiresAt, priceTier?.Trim(), territory?.Trim(), grantsPortalAccess);
    }

    public void Redeem(DateTimeOffset at)
    {
        if (!IsRedeemable(at)) throw new InvalidOperationException("Connection code is expired, exhausted, or revoked.");
        RedeemedUses++;
    }
}
