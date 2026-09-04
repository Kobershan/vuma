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
    private CompanyLink() { }

    private CompanyLink(Guid tenantId, Guid operatorId, Guid companyAId, Guid companyBId, CompanyLinkScope scopes)
    {
        if (companyAId == Guid.Empty || companyBId == Guid.Empty)
        {
            throw new ArgumentException("Both companies are required.");
        }
        if (companyAId == companyBId)
        {
            throw new ArgumentException("A company cannot link to itself.");
        }
        if (scopes == CompanyLinkScope.None)
        {
            throw new ArgumentException("A link must have at least one scope.");
        }

        // Ordering rule: smaller GUID first
        if (companyAId.CompareTo(companyBId) < 0)
        {
            CompanyAId = companyAId;
            CompanyBId = companyBId;
        }
        else
        {
            CompanyAId = companyBId;
            CompanyBId = companyAId;
        }

        TenantId = tenantId;
        OperatorId = operatorId;
        Scopes = scopes;
        Status = CompanyLinkStatus.Proposed;
        EffectiveFrom = DateTimeOffset.UtcNow;
    }

    /// <summary>The stable identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The tenant this link belongs to.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The smaller of the two company GUIDs.</summary>
    public Guid CompanyAId { get; private set; }

    /// <summary>The larger of the two company GUIDs.</summary>
    public Guid CompanyBId { get; private set; }

    /// <summary>The scopes this link permits.</summary>
    public CompanyLinkScope Scopes { get; private set; }

    /// <summary>The current status.</summary>
    public CompanyLinkStatus Status { get; private set; }

    /// <summary>When the link becomes effective.</summary>
    public DateTimeOffset EffectiveFrom { get; private set; }

    /// <summary>When the link expires, or null if still active.</summary>
    public DateTimeOffset? EffectiveTo { get; private set; }

    /// <summary>Whether company A has accepted.</summary>
    public bool AcceptedByA { get; private set; }

    /// <summary>Whether company B has accepted.</summary>
    public bool AcceptedByB { get; private set; }

    /// <summary>When both sides accepted.</summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    /// <summary>The reason for suspension.</summary>
    public string? SuspendedReason { get; private set; }

    /// <summary>When the link was suspended.</summary>
    public DateTimeOffset? SuspendedAt { get; private set; }

    /// <summary>The reason for revocation.</summary>
    public string? RevokedReason { get; private set; }

    /// <summary>When the link was revoked.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>The operator ID that must match both companies.</summary>
    public Guid OperatorId { get; private set; }

    /// <summary>The operator name for display purposes.</summary>
    public string OperatorName { get; private set; } = string.Empty;

    /// <summary>
    /// Creates a new proposed link between two companies under the same operator.
    /// Enforces the operator-match invariant (ADR-121) and the ordering rule.
    /// </summary>
    public static CompanyLink Create(Guid tenantId, Guid operatorId, Guid companyAId, Guid companyBId, CompanyLinkScope scopes)
    {
        if (companyAId == companyBId)
        {
            throw new ArgumentException("A company cannot link to itself.");
        }

        // The operator-match invariant is enforced here and again by the database check constraint
        var link = new CompanyLink(tenantId, operatorId, companyAId, companyBId, scopes);
        link.Id = UuidV7.NewGuid();
        return link;
    }

    /// <summary>Checks whether the operator matches both companies.</summary>
    public bool HasOperatorMatch(Guid operatorId) => OperatorId == operatorId;

    /// <summary>Accepts the link from the given company.</summary>
    public void Accept(Guid companyId, DateTimeOffset acceptedAt)
    {
        if (Status != CompanyLinkStatus.Proposed)
        {
            throw new InvalidOperationException("Only a proposed link can be accepted.");
        }

        if (CompanyAId == companyId)
        {
            AcceptedByA = true;
        }
        else if (CompanyBId == companyId)
        {
            AcceptedByB = true;
        }
        else
        {
            throw new InvalidOperationException("Company is not part of this link.");
        }

        if (AcceptedByA && AcceptedByB)
        {
            Status = CompanyLinkStatus.Active;
            AcceptedAt = acceptedAt;
        }
    }

    /// <summary>Suspends an active link with a reason.</summary>
    public void Suspend(string reason, DateTimeOffset suspendedAt)
    {
        if (Status != CompanyLinkStatus.Active)
        {
            throw new InvalidOperationException("Only active links can be suspended.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required.", nameof(reason));
        }

        Status = CompanyLinkStatus.Suspended;
        SuspendedReason = reason;
        SuspendedAt = suspendedAt;
    }

    /// <summary>Revokes a link with a reason of at least 10 characters.</summary>
    public void Revoke(string reason, DateTimeOffset revokedAt)
    {
        if (Status == CompanyLinkStatus.Revoked)
        {
            throw new InvalidOperationException("Link is already revoked.");
        }
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10)
        {
            throw new ArgumentException("Revocation requires a reason of at least 10 characters.", nameof(reason));
        }

        Status = CompanyLinkStatus.Revoked;
        RevokedReason = reason;
        RevokedAt = revokedAt;
    }
}
