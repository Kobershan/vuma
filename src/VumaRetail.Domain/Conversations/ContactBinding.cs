#pragma warning disable CS1591
#pragma warning disable IDE0011
using System.Security.Cryptography;
using System.Text;
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Conversations;

/// <summary>Tenant-owned binding between a channel address and an existing contact.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class ContactBinding : Entity
{
    private ContactBinding() { }
    public ContactBinding(Guid tenantId, string address, Guid contactId, ConversationChannel channel)
        : base(tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        Address = address.Trim();
        ContactId = contactId;
        Channel = channel;
    }
    public string Address { get; private set; } = string.Empty;
    public Guid ContactId { get; private set; }
    public ConversationChannel Channel { get; private set; }
    public BindingVerificationState VerificationState { get; private set; } = BindingVerificationState.Unverified;
    public DateTimeOffset? VerifiedAt { get; private set; }
    public ConversationConsentState ConsentState { get; private set; } = ConversationConsentState.NotAsked;
    public DateTimeOffset? LockedUntil { get; private set; }

    public void Verify(DateTimeOffset at) { VerificationState = BindingVerificationState.Verified; VerifiedAt = at; LockedUntil = null; }
    public void Revoke() => VerificationState = BindingVerificationState.Revoked;
    public void GrantConsent() => ConsentState = ConversationConsentState.Granted;
    public void WithdrawConsent() => ConsentState = ConversationConsentState.Withdrawn;
    public void Lock(DateTimeOffset until) { VerificationState = BindingVerificationState.Locked; LockedUntil = until; }
    /// <summary>Returns whether the binding is verified, unlocked, and freshly verified.</summary>
    public bool IsUsable(DateTimeOffset at, TimeSpan? verificationFreshness = null)
    {
        TimeSpan freshness = verificationFreshness ?? TimeSpan.FromHours(24);
        return VerificationState == BindingVerificationState.Verified
            && VerifiedAt is not null
            && at >= VerifiedAt.Value
            && at - VerifiedAt.Value <= freshness
            && (LockedUntil is null || LockedUntil <= at);
    }
}

/// <summary>One-time challenge; only a hash of the OTP is retained.</summary>
public sealed class VerificationChallenge : Entity
{
    private VerificationChallenge() { }
    public VerificationChallenge(Guid tenantId, Guid bindingId, string otp, DateTimeOffset issuedAt, TimeSpan? lifetime = null)
        : base(tenantId)
    {
        BindingId = bindingId;
        Hash = HashOtp(otp);
        IssuedAt = issuedAt;
        ExpiresAt = issuedAt.Add(lifetime ?? TimeSpan.FromMinutes(10));
    }
    public Guid BindingId { get; private set; }
    public string Hash { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public int Attempts { get; private set; }
    public bool Used { get; private set; }
    public bool Verify(string otp, DateTimeOffset at)
    {
        if (Used || at > ExpiresAt || Attempts >= 3) { return false; }
        Attempts++;
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Hash), Convert.FromHexString(HashOtp(otp)))) { return false; }
        Used = true;
        return true;
    }
    private static string HashOtp(string otp) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(otp)));
}

/// <summary>Order lines held until the customer confirms a pro forma submission.</summary>
public sealed record BotOrderDraft(Guid ConversationId, Guid? CompanyId, IReadOnlyList<BotOrderLine> Lines, string? ApiQuote);
public sealed record BotOrderLine(Guid ItemId, string Description, decimal Quantity, string UnitOfMeasure);
