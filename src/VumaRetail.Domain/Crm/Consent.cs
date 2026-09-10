using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Crm;

/// <summary>
/// One customer's opt-in/opt-out record for one processing purpose (Stage 19). Exactly one row
/// per (type, customer). POPIA: captured per purpose, withdrawable at any time, expirable, with
/// a full audit trail via the platform audit entries.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class Consent : Entity
{
    private Consent()
    {
    }

    /// <summary>Opens a consent record. Starts <c>NotAsked</c> until given.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The owning company.</param>
    /// <param name="customerId">The customer it covers.</param>
    /// <param name="type">The processing purpose.</param>
    /// <param name="expiresAt">Expiry, if the consent is time-boxed.</param>
    /// <param name="storeId">The owning store, if any.</param>
    public Consent(
        Guid tenantId,
        Guid companyId,
        Guid customerId,
        ConsentType type,
        DateTimeOffset? expiresAt = null,
        Guid? storeId = null)
        : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        CustomerId = customerId;
        Type = type;
        State = ConsentState.NotAsked;
        ExpiresAt = expiresAt;
    }

    /// <summary>The customer it covers. A plain id, never a cross-schema FK.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The processing purpose.</summary>
    public ConsentType Type { get; private set; }

    /// <summary>Current state.</summary>
    public ConsentState State { get; private set; }

    /// <summary>When consent was given, UTC.</summary>
    public DateTimeOffset? GrantedAt { get; private set; }

    /// <summary>When it was withdrawn, UTC.</summary>
    public DateTimeOffset? WithdrawnAt { get; private set; }

    /// <summary>When it expires, if time-boxed.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Why it was withdrawn, if a reason was recorded.</summary>
    public string? WithdrawalReason { get; private set; }

    /// <summary>What affirmative action captured it (the UI element or form). POPIA voluntariness.</summary>
    public string? Source { get; private set; }

    /// <summary>Records affirmative consent.</summary>
    /// <param name="grantedAt">When, UTC. From <c>IClock</c>.</param>
    /// <param name="source">What affirmative action captured it.</param>
    /// <param name="capturedBy">Who captured it.</param>
    /// <exception cref="DuplicateConsentException">Already given and still valid.</exception>
    public void Give(DateTimeOffset grantedAt, string source, string capturedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(capturedBy);
        if (State == ConsentState.Given && IsValid(grantedAt))
        {
            throw new DuplicateConsentException();
        }

        State = ConsentState.Given;
        GrantedAt = grantedAt;
        WithdrawnAt = null;
        WithdrawalReason = null;
        Source = source;
    }

    /// <summary>Withdraws consent. Immediate — no grace period for marketing.</summary>
    /// <param name="withdrawnAt">When, UTC. From <c>IClock</c>.</param>
    /// <param name="withdrawnBy">Who recorded the withdrawal.</param>
    /// <param name="reason">Why, if recorded.</param>
    public void Withdraw(DateTimeOffset withdrawnAt, string withdrawnBy, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(withdrawnBy);
        State = ConsentState.Withdrawn;
        WithdrawnAt = withdrawnAt;
        WithdrawalReason = reason;
    }

    /// <summary>Whether consent is effective at the given instant.</summary>
    /// <param name="at">The instant to evaluate at, UTC.</param>
    /// <returns>True only for <c>Given</c> with no passed expiry.</returns>
    public bool IsValid(DateTimeOffset at)
        => State == ConsentState.Given && (ExpiresAt is null || ExpiresAt > at);
}
