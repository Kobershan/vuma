using VumaRetail.Domain.Primitives;

// Registry domain entities for Stage 06d (Group services) and 06e (Trading group)
#pragma warning disable CS1591
#pragma warning disable IDE0011
namespace VumaRetail.Domain.Registry;

// ========== Stage 06d: Credit Groups ==========

public sealed class CreditGroup
{
    private CreditGroup() { }
    public CreditGroup(Guid tenantId, string name, string direction, decimal limit, string currency)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(direction)) throw new ArgumentException("A direction is required.", nameof(direction));
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("A currency is required.", nameof(currency));
        Id = UuidV7.NewGuid(); TenantId = tenantId; Name = name.Trim(); Direction = direction.Trim(); Limit = limit; Currency = currency.Trim();
    }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Direction { get; private set; } = string.Empty;
    public decimal Limit { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public List<CreditGroupMember> Members { get; private set; } = [];
}

public sealed class CreditGroupMember
{
    private CreditGroupMember() { }
    public CreditGroupMember(Guid creditGroupId, Guid companyId, decimal? subLimit, Guid tenantId)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (creditGroupId == Guid.Empty) throw new ArgumentException("A credit group is required.", nameof(creditGroupId));
        if (companyId == Guid.Empty) throw new ArgumentException("A company is required.", nameof(companyId));
        CreditGroupId = creditGroupId; CompanyId = companyId; SubLimit = subLimit; TenantId = tenantId;
    }
    public Guid TenantId { get; private set; }
    public Guid CreditGroupId { get; private set; }
    public Guid CompanyId { get; private set; }
    public decimal? SubLimit { get; private set; }
}

public enum CreditHoldState { Held, Confirmed, Released, Expired }

public sealed class CreditHold
{
    private CreditHold() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CreditGroupId { get; private set; }
    public Guid CompanyId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string DocumentReference { get; private set; } = string.Empty;
    public CreditHoldState State { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }

    public static CreditHold Create(Guid tenantId, Guid creditGroupId, Guid companyId, decimal amount, string currency, string documentReference, DateTimeOffset expiresAt)
    {
        return new CreditHold
        {
            Id = UuidV7.NewGuid(),
            TenantId = tenantId,
            CreditGroupId = creditGroupId,
            CompanyId = companyId,
            Amount = amount,
            Currency = currency,
            DocumentReference = documentReference,
            State = CreditHoldState.Held,
            ExpiresAt = expiresAt
        };
    }

    public void Confirm(DateTimeOffset confirmedAt)
    {
        if (State != CreditHoldState.Held) throw new InvalidOperationException("Only held holds can be confirmed.");
        State = CreditHoldState.Confirmed;
        ConfirmedAt = confirmedAt;
    }

    public void Release(DateTimeOffset releasedAt)
    {
        if (State != CreditHoldState.Held) throw new InvalidOperationException("Only held holds can be released.");
        State = CreditHoldState.Released;
        ReleasedAt = releasedAt;
    }

    public void Expire(DateTimeOffset expiredAt)
    {
        if (State != CreditHoldState.Held) throw new InvalidOperationException("Only held holds can expire.");
        State = CreditHoldState.Expired;
        ExpiredAt = expiredAt;
    }
}

public sealed class CreditExposureEntry
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CreditGroupId { get; set; }
    public Guid CompanyId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string DocumentReference { get; set; } = string.Empty;
    public DateTimeOffset ConfirmedAt { get; set; }
}

// ========== Stage 06d: Barcode Routing Index ==========

public sealed class CatalogRoutingIndexEntry
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyCode { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public Guid? VariantId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset AsAt { get; set; }
    public bool IsRetired { get; set; }

    public void Update(Guid itemId, Guid? variantId, string itemCode, string description, DateTimeOffset asAt)
    {
        ItemId = itemId;
        VariantId = variantId;
        ItemCode = itemCode;
        Description = description;
        AsAt = asAt;
        IsRetired = false;
    }
}

// ========== Stage 06e: Company Links ==========

/// <summary>Scopes that companies may cooperate under.</summary>
[Flags]
public enum CompanyLinkScope
{
    None = 0,
    SharedFloor = 1,
    SharedTill = 2,
    SharedCredit = 4,
    SharedReceipting = 8,
    SharedSourcing = 16,
    SharedPicking = 32,
    SharedReporting = 64,
}

/// <summary>The status of a company link.</summary>
public enum CompanyLinkStatus
{
    Proposed,
    Accepted,
    Active,
    Suspended,
    Revoked,
}

public sealed class CompanyLink
{
    public CompanyLink() { }
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CompanyAId { get; set; }
    public Guid CompanyBId { get; set; }
    public CompanyLinkScope Scopes { get; set; }
    public CompanyLinkStatus Status { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public bool AcceptedByA { get; set; }
    public bool AcceptedByB { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public string? SuspendedReason { get; set; }
    public DateTimeOffset? SuspendedAt { get; set; }
    public string? RevokedReason { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
}
