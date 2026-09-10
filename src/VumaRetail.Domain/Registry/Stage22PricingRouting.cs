#pragma warning disable CS1591
#pragma warning disable IDE0011
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>Explicit ownership route for a SKU or barcode at a shared premises.</summary>
public sealed class PremisesSkuRouting
{
    private PremisesSkuRouting() { }
    private PremisesSkuRouting(Guid tenantId, Guid premisesId, string key, Guid companyId, bool isBarcode)
    {
        if (tenantId == Guid.Empty || premisesId == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Tenant, premises and company are required.");
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A SKU or barcode is required.", nameof(key));
        TenantId = tenantId; PremisesId = premisesId; SkuOrBarcode = key.Trim(); CompanyId = companyId; IsBarcode = isBarcode;
    }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PremisesId { get; private set; }
    public string SkuOrBarcode { get; private set; } = string.Empty;
    public Guid CompanyId { get; private set; }
    public bool IsBarcode { get; private set; }
    public static PremisesSkuRouting Create(Guid tenantId, Guid premisesId, string key, Guid companyId, bool isBarcode) => new(tenantId, premisesId, key, companyId, isBarcode) { Id = UuidV7.NewGuid() };
    public static void EnsureUnique(IEnumerable<PremisesSkuRouting> routes, PremisesSkuRouting candidate)
    {
        if (routes.Any(x => x.TenantId == candidate.TenantId && x.PremisesId == candidate.PremisesId && x.IsBarcode == candidate.IsBarcode && string.Equals(x.SkuOrBarcode, candidate.SkuOrBarcode, StringComparison.OrdinalIgnoreCase) && x.CompanyId != candidate.CompanyId))
            throw new InvalidOperationException("A bare barcode can route to only one company at a premises.");
    }
}

/// <summary>Retail group price; transfer cost is deliberately not part of this model.</summary>
public sealed record GroupRetailPrice(Guid BusinessId, Guid ItemId, decimal BasePrice, decimal? LocalOverride)
{
    public decimal EffectivePrice => LocalOverride ?? BasePrice;
}

/// <summary>Wholesale franchise price with a company-specific fallback override.</summary>
public sealed record FranchiseWholesalePrice(Guid BrandCompanyId, Guid FranchiseeCompanyId, Guid ItemId, decimal FlatPrice, decimal? FranchiseeOverride)
{
    public decimal EffectivePrice => FranchiseeOverride ?? FlatPrice;
}

/// <summary>Price owned by one company in a shared-premises basket.</summary>
public sealed record SharedPremisesRetailPrice(Guid PremisesId, Guid CompanyId, Guid ItemId, decimal RetailPrice);
