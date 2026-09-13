#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Ecommerce;

/// <summary>Versioned sell-facing product projection for one registered storefront channel.</summary>
[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class PublishedProduct : Entity
{
    private PublishedProduct(Guid tenantId, Guid companyId, Guid channelId, Guid? itemId, Guid? variantId,
        string sku, string name, string? description, decimal price, string currency, decimal available,
        DateTimeOffset availabilityAsAt, int version)
        : base(tenantId)
    {
        AssignCompany(companyId);
        ChannelConnectionId = channelId;
        ItemId = itemId;
        ItemVariantId = variantId;
        Sku = sku.Trim();
        Name = name.Trim();
        Description = description?.Trim();
        Price = price;
        Currency = currency.Trim().ToUpperInvariant();
        Available = available;
        AvailabilityAsAt = availabilityAsAt;
        Version = version;
        PublishedAt = availabilityAsAt;
    }

    private PublishedProduct() { }

    public Guid ChannelConnectionId { get; private set; }
    public Guid? ItemId { get; private set; }
    public Guid? ItemVariantId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal Available { get; private set; }
    public DateTimeOffset AvailabilityAsAt { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }

    public static PublishedProduct Publish(Guid tenantId, Guid companyId, Guid channelId, Guid? itemId, Guid? variantId,
        string sku, string name, string? description, decimal price, string currency, decimal available,
        DateTimeOffset asAt, int version)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || channelId == Guid.Empty)
        {
            throw new ArgumentException("A published product requires tenant, company and channel identities.");
        }
        if ((itemId is null) == (variantId is null))
        {
            throw new ArgumentException("Exactly one item or variant is required.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (price < 0m || available < 0m || version < 1)
        {
            throw new ArgumentException("Product price, availability or version is invalid.");
        }
        if (currency.Trim().Length != 3)
        {
            throw new ArgumentException("Currency must be an ISO 4217 code.");
        }
        return new PublishedProduct(tenantId, companyId, channelId, itemId, variantId, sku, name, description, price,
            currency, available, asAt, version);
    }
}
