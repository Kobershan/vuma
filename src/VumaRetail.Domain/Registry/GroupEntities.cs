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

    private CompanyLink(Guid tenantId, Guid operatorId, Guid companyAId, Guid companyBId, CompanyLinkScope scopes, DateTimeOffset effectiveFrom)
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

        // Ordering rule: smaller GUID first, so one pair can hold at most one link row.
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
        EffectiveFrom = effectiveFrom;
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

    /// <summary>Who accepted for company A, in audit-principal form.</summary>
    public string? AcceptedByABy { get; private set; }

    /// <summary>Who accepted for company B, in audit-principal form.</summary>
    public string? AcceptedByBBy { get; private set; }

    /// <summary>When company A accepted.</summary>
    public DateTimeOffset? AcceptedByAAt { get; private set; }

    /// <summary>When company B accepted.</summary>
    public DateTimeOffset? AcceptedByBAt { get; private set; }

    /// <summary>The licence fingerprint company A accepted under.</summary>
    public string? AcceptedByAFingerprint { get; private set; }

    /// <summary>The licence fingerprint company B accepted under.</summary>
    public string? AcceptedByBFingerprint { get; private set; }

    /// <summary>When both sides had accepted and the link became active.</summary>
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
    /// Enforces the ordering rule. The operator-match invariant (ADR-121) is enforced by the
    /// caller, which loads both companies: the link only records the operator it was proposed
    /// under, and the registry trigger refuses a row whose operator differs from either company.
    /// </summary>
    public static CompanyLink Create(Guid tenantId, Guid operatorId, Guid companyAId, Guid companyBId, CompanyLinkScope scopes, DateTimeOffset effectiveFrom, string? operatorName = null)
    {
        if (companyAId == companyBId)
        {
            throw new ArgumentException("A company cannot link to itself.");
        }
        if (operatorId == Guid.Empty)
        {
            throw new ArgumentException("An operator identifier is required.", nameof(operatorId));
        }

        var link = new CompanyLink(tenantId, operatorId, companyAId, companyBId, scopes, effectiveFrom);
        link.Id = UuidV7.NewGuid();
        link.OperatorName = operatorName?.Trim() ?? string.Empty;
        return link;
    }

    /// <summary>Checks whether the operator matches both companies.</summary>
    public bool HasOperatorMatch(Guid operatorId) => OperatorId == operatorId;

    /// <summary>
    /// Accepts the link from the given company, recording who accepted under which licence.
    /// </summary>
    /// <remarks>
    /// The first acceptance moves the link to <c>Accepted</c> — which grants nothing. The second
    /// moves it to <c>Active</c>. Re-accepting from a side that already accepted is a no-op so a
    /// retried request cannot corrupt the record.
    /// </remarks>
    public void Accept(Guid companyId, string acceptedBy, string licenceFingerprint, DateTimeOffset acceptedAt)
    {
        if (Status is not (CompanyLinkStatus.Proposed or CompanyLinkStatus.Accepted))
        {
            throw new InvalidOperationException("Only a proposed or partially accepted link can be accepted.");
        }
        if (string.IsNullOrWhiteSpace(acceptedBy))
        {
            throw new ArgumentException("The accepting principal is required.", nameof(acceptedBy));
        }
        if (string.IsNullOrWhiteSpace(licenceFingerprint))
        {
            throw new ArgumentException("The licence fingerprint at acceptance is required.", nameof(licenceFingerprint));
        }

        if (CompanyAId == companyId)
        {
            if (AcceptedByA)
            {
                return;
            }

            AcceptedByA = true;
            AcceptedByABy = acceptedBy.Trim();
            AcceptedByAAt = acceptedAt;
            AcceptedByAFingerprint = licenceFingerprint.Trim();
        }
        else if (CompanyBId == companyId)
        {
            if (AcceptedByB)
            {
                return;
            }

            AcceptedByB = true;
            AcceptedByBBy = acceptedBy.Trim();
            AcceptedByBAt = acceptedAt;
            AcceptedByBFingerprint = licenceFingerprint.Trim();
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
        else
        {
            Status = CompanyLinkStatus.Accepted;
        }
    }

    /// <summary>Suspends an active link with a reason. Reversible via <see cref="Resume"/>.</summary>
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
        SuspendedReason = reason.Trim();
        SuspendedAt = suspendedAt;
    }

    /// <summary>Resumes a suspended link. The dispute mechanism's way back.</summary>
    public void Resume()
    {
        if (Status != CompanyLinkStatus.Suspended)
        {
            throw new InvalidOperationException("Only a suspended link can be resumed.");
        }

        Status = CompanyLinkStatus.Active;
        SuspendedReason = null;
        SuspendedAt = null;
    }

    /// <summary>
    /// Revokes a link with a reason of at least 10 characters. Final: history stands, and the pair
    /// cannot be re-proposed — a new arrangement needs a vendor-side decision, not a re-click.
    /// </summary>
    public void Revoke(string reason, DateTimeOffset revokedAt)
    {
        if (Status == CompanyLinkStatus.Revoked)
        {
            throw new InvalidOperationException("Link is already revoked.");
        }
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
        {
            throw new ArgumentException("Revocation requires a reason of at least 10 characters.", nameof(reason));
        }

        Status = CompanyLinkStatus.Revoked;
        RevokedReason = reason.Trim();
        RevokedAt = revokedAt;
        EffectiveTo = revokedAt;
    }
}
