#pragma warning disable CS1591
#pragma warning disable IDE0011
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>Registry-only grouping label and mutable business taxonomy.</summary>
public sealed class BusinessRegistration
{
    private BusinessRegistration() { }
    private BusinessRegistration(Guid id, Guid tenantId, string name, BusinessType type, DateTimeOffset changedAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty) throw new ArgumentException("Business and tenant identifiers are required.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A business name is required.", nameof(name));
        Id = id; TenantId = tenantId; Name = name.Trim(); Type = type; ChangedAt = changedAt;
    }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public BusinessType Type { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public static BusinessRegistration Create(Guid businessId, Guid tenantId, string name, BusinessType type, DateTimeOffset changedAt) => new(businessId, tenantId, name, type, changedAt);
    public void ChangeType(BusinessType type, DateTimeOffset changedAt) { Type = type; ChangedAt = changedAt; }
}

/// <summary>Thin registry relationship between a business label and a legal company.</summary>
public sealed class BusinessCompanyMembership
{
    private BusinessCompanyMembership() { }
    private BusinessCompanyMembership(Guid tenantId, Guid businessId, Guid companyId)
    {
        if (tenantId == Guid.Empty || businessId == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Tenant, business and company identifiers are required.");
        Id = UuidV7.NewGuid(); TenantId = tenantId; BusinessId = businessId; CompanyId = companyId;
    }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid CompanyId { get; private set; }
    public static BusinessCompanyMembership Create(Guid tenantId, Guid businessId, Guid companyId) => new(tenantId, businessId, companyId);
}
