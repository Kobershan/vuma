#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Connect;

/// <summary>Explicit supplier-to-retailer portal access. It never grants general tenant data access.</summary>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class SupplierPortalGrant : Entity
{
    private SupplierPortalGrant(Guid retailerTenantId, Guid supplierTenantId, Guid connectionId,
        Guid contactId, string accessRole, DateTimeOffset grantedAt) : base(retailerTenantId)
    {
        RetailerTenantId = retailerTenantId;
        SupplierTenantId = supplierTenantId;
        ConnectionId = connectionId;
        ContactId = contactId;
        AccessRole = accessRole.Trim();
        GrantedAt = grantedAt;
    }

    private SupplierPortalGrant() { }

    public Guid RetailerTenantId { get; private set; }
    public Guid SupplierTenantId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public Guid ContactId { get; private set; }
    public string AccessRole { get; private set; } = string.Empty;
    public DateTimeOffset GrantedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public bool IsActive => RevokedAt is null && DeletedAt is null;

    public static SupplierPortalGrant Create(Guid supplierTenantId, Guid retailerTenantId, Guid connectionId,
        Guid contactId, string accessRole, DateTimeOffset grantedAt)
    {
        if (supplierTenantId == Guid.Empty || retailerTenantId == Guid.Empty || supplierTenantId == retailerTenantId
            || connectionId == Guid.Empty || contactId == Guid.Empty || string.IsNullOrWhiteSpace(accessRole))
        {
            throw new ArgumentException("A portal grant requires two tenants, a connection, a contact and a role.");
        }
        return new(retailerTenantId, supplierTenantId, connectionId, contactId, accessRole, grantedAt);
    }

    public void Revoke(Guid actorTenantId, DateTimeOffset revokedAt)
    {
        if (actorTenantId != RetailerTenantId && actorTenantId != SupplierTenantId)
        {
            throw new UnauthorizedAccessException("Only a party to the connection can revoke portal access.");
        }
        if (!IsActive)
        {
            throw new InvalidOperationException("Portal access is already revoked.");
        }
        RevokedAt = revokedAt;
    }
}
