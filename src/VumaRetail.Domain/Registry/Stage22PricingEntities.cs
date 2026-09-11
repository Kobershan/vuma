#pragma warning disable CS1591
#pragma warning disable IDE0011
#pragma warning disable CA1062
using VumaRetail.Domain.Entities;

namespace VumaRetail.Domain.Registry;

/// <summary>Tenant-owned persisted price for an owned business location.</summary>
public sealed class GroupRetailPriceRow : Entity
{
    private GroupRetailPriceRow() { }

    private GroupRetailPriceRow(Guid tenantId, Guid businessId, Guid companyId, Guid itemId,
        decimal basePrice, decimal? localOverride, string currency)
        : base(tenantId)
    {
        if (businessId == Guid.Empty || companyId == Guid.Empty || itemId == Guid.Empty)
            throw new ArgumentException("Business, company and item are required.");
        ValidatePrices(basePrice, localOverride);
        BusinessId = businessId;
        CompanyId = companyId;
        ItemId = itemId;
        BasePrice = basePrice;
        LocalOverride = localOverride;
        Currency = NormalizeCurrency(currency);
    }

    public Guid BusinessId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal BasePrice { get; private set; }
    public decimal? LocalOverride { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal EffectivePrice => LocalOverride ?? BasePrice;

    public static GroupRetailPriceRow Define(Guid tenantId, Guid businessId, Guid companyId, Guid itemId,
        decimal basePrice, decimal? localOverride, string currency)
        => new(tenantId, businessId, companyId, itemId, basePrice, localOverride, currency);

    public void Replace(decimal basePrice, decimal? localOverride, string currency)
    {
        ValidatePrices(basePrice, localOverride);
        BasePrice = basePrice;
        LocalOverride = localOverride;
        Currency = NormalizeCurrency(currency);
    }

    private static void ValidatePrices(decimal basePrice, decimal? localOverride)
    {
        if (basePrice < 0m || localOverride is < 0m) throw new ArgumentOutOfRangeException(nameof(basePrice));
    }

    private static string NormalizeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            throw new ArgumentException("A three-letter currency is required.", nameof(currency));
        return currency.Trim().ToUpperInvariant();
    }
}

/// <summary>Tenant-owned flat wholesale price with a franchisee-specific override.</summary>
public sealed class FranchiseWholesalePriceRow : Entity
{
    private FranchiseWholesalePriceRow() { }

    private FranchiseWholesalePriceRow(Guid tenantId, Guid brandCompanyId, Guid franchiseeCompanyId, Guid itemId,
        decimal flatPrice, decimal? franchiseeOverride, string currency)
        : base(tenantId)
    {
        if (brandCompanyId == Guid.Empty || franchiseeCompanyId == Guid.Empty || itemId == Guid.Empty)
            throw new ArgumentException("Brand, franchisee and item are required.");
        if (brandCompanyId == franchiseeCompanyId) throw new ArgumentException("Brand and franchisee must differ.");
        if (flatPrice < 0m || franchiseeOverride is < 0m) throw new ArgumentOutOfRangeException(nameof(flatPrice));
        BrandCompanyId = brandCompanyId;
        FranchiseeCompanyId = franchiseeCompanyId;
        ItemId = itemId;
        FlatPrice = flatPrice;
        FranchiseeOverride = franchiseeOverride;
        Currency = currency.Trim().ToUpperInvariant();
        if (Currency.Length != 3) throw new ArgumentException("A three-letter currency is required.", nameof(currency));
    }

    public Guid BrandCompanyId { get; private set; }
    public Guid FranchiseeCompanyId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal FlatPrice { get; private set; }
    public decimal? FranchiseeOverride { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal EffectivePrice => FranchiseeOverride ?? FlatPrice;

    public static FranchiseWholesalePriceRow Define(Guid tenantId, Guid brandCompanyId, Guid franchiseeCompanyId,
        Guid itemId, decimal flatPrice, decimal? franchiseeOverride, string currency)
        => new(tenantId, brandCompanyId, franchiseeCompanyId, itemId, flatPrice, franchiseeOverride, currency);

    public void Replace(decimal flatPrice, decimal? franchiseeOverride, string currency)
    {
        if (flatPrice < 0m || franchiseeOverride is < 0m) throw new ArgumentOutOfRangeException(nameof(flatPrice));
        FlatPrice = flatPrice;
        FranchiseeOverride = franchiseeOverride;
        Currency = currency.Trim().ToUpperInvariant();
        if (Currency.Length != 3) throw new ArgumentException("A three-letter currency is required.", nameof(currency));
    }
}

/// <summary>Company-owned retail price for one SKU at one shared premises.</summary>
public sealed class SharedPremisesRetailPriceRow : Entity
{
    private SharedPremisesRetailPriceRow() { }

    private SharedPremisesRetailPriceRow(Guid tenantId, Guid premisesId, Guid companyId, Guid itemId,
        decimal retailPrice, string currency)
        : base(tenantId)
    {
        if (premisesId == Guid.Empty || companyId == Guid.Empty || itemId == Guid.Empty)
            throw new ArgumentException("Premises, company and item are required.");
        if (retailPrice < 0m) throw new ArgumentOutOfRangeException(nameof(retailPrice));
        RetailPrice = retailPrice;
        PremisesId = premisesId;
        CompanyId = companyId;
        ItemId = itemId;
        Currency = currency.Trim().ToUpperInvariant();
        if (Currency.Length != 3) throw new ArgumentException("A three-letter currency is required.", nameof(currency));
    }

    public Guid PremisesId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal RetailPrice { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    public static SharedPremisesRetailPriceRow Define(Guid tenantId, Guid premisesId, Guid companyId, Guid itemId,
        decimal retailPrice, string currency)
        => new(tenantId, premisesId, companyId, itemId, retailPrice, currency);

    public void Replace(decimal retailPrice, string currency)
    {
        if (retailPrice < 0m) throw new ArgumentOutOfRangeException(nameof(retailPrice));
        RetailPrice = retailPrice;
        Currency = currency.Trim().ToUpperInvariant();
        if (Currency.Length != 3) throw new ArgumentException("A three-letter currency is required.", nameof(currency));
    }
}
