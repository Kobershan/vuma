#pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Ecommerce;

/// <summary>A tenant- and channel-owned basket whose line prices are always advisory.</summary>
[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class CommerceBasket : Entity
{
    private CommerceBasket(Guid tenantId, Guid companyId, Guid channelId, string ownerKey, DateTimeOffset createdAt)
        : base(tenantId)
    {
        AssignCompany(companyId);
        ChannelConnectionId = channelId;
        OwnerKey = ownerKey.Trim();
        CreatedAtUtc = createdAt;
        Status = CommerceBasketStatus.Open;
    }

    private CommerceBasket() { }

    public Guid ChannelConnectionId { get; private set; }
    public string OwnerKey { get; private set; } = string.Empty;
    public CommerceBasketStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static CommerceBasket Open(Guid tenantId, Guid companyId, Guid channelId, string ownerKey, DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || channelId == Guid.Empty)
        {
            throw new ArgumentException("A basket requires tenant, company and channel identities.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        return new CommerceBasket(tenantId, companyId, channelId, ownerKey, createdAt);
    }

    public void Close() => Status = CommerceBasketStatus.Closed;
}

public enum CommerceBasketStatus
{
    Open = 1,
    CheckoutPending = 2,
    Closed = 3
}
