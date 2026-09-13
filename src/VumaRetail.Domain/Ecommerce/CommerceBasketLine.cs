#pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Ecommerce;

[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class CommerceBasketLine : Entity
{
    private CommerceBasketLine(Guid tenantId, Guid companyId, Guid basketId, Guid productId, decimal quantity,
        decimal advisoryUnitPrice, decimal authoritativeUnitPrice, string currency)
        : base(tenantId)
    {
        AssignCompany(companyId);
        BasketId = basketId;
        PublishedProductId = productId;
        Quantity = quantity;
        AdvisoryUnitPrice = advisoryUnitPrice;
        AuthoritativeUnitPrice = authoritativeUnitPrice;
        Currency = currency.Trim().ToUpperInvariant();
    }

    private CommerceBasketLine() { }

    public Guid BasketId { get; private set; }
    public Guid PublishedProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal AdvisoryUnitPrice { get; private set; }
    public decimal AuthoritativeUnitPrice { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    public static CommerceBasketLine Add(Guid tenantId, Guid companyId, Guid basketId, Guid productId,
        decimal quantity, decimal advisoryUnitPrice, decimal authoritativeUnitPrice, string currency)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || basketId == Guid.Empty || productId == Guid.Empty)
        {
            throw new ArgumentException("A basket line requires tenant, company, basket and product identities.");
        }
        if (quantity <= 0m || advisoryUnitPrice < 0m || authoritativeUnitPrice < 0m)
        {
            throw new ArgumentException("Basket quantity must be positive and price cannot be negative.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (currency.Trim().Length != 3)
        {
            throw new ArgumentException("Currency must be an ISO 4217 code.");
        }
        return new CommerceBasketLine(tenantId, companyId, basketId, productId, quantity, advisoryUnitPrice, authoritativeUnitPrice, currency);
    }
}
