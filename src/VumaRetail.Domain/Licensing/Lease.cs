using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Licensing;

/// <summary>
/// The short-lived signed token the software actually runs on (<c>LICENSING.md</c> §2).
/// </summary>
/// <remarks>
/// <para>
/// This is the mechanism that makes "monthly licence" real without requiring a permanently online
/// store. A lease is derived from the current licence, lives 72 hours, and is refreshed on start and
/// every 24 hours — so a shop with a dead fibre link keeps trading on the last one it was given.
/// </para>
/// <para>
/// <b>An expired lease does not restrict anybody.</b> That is the single most counter-intuitive thing
/// in this module and it is deliberate: ADR-028's fourth rule is that a vendor-side outage never
/// restricts anyone, and a lease that restricted on expiry would do exactly that 72 hours into any
/// control-plane outage. What restricts is the Path A tolerance window measured from
/// <see cref="Activation.LastContactAt"/> — a fortnight, not three days — or a Path B answer the
/// control plane gave in so many words.
/// </para>
/// <para>
/// <see cref="IImmutableRecord"/>: every refresh writes a new row. The history is what lets support
/// answer "when did this store last hear that it was fine", which is the first question in every
/// argument about a restriction.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.AppendOnly)]
public sealed class Lease : Entity, IImmutableRecord
{
    private Lease(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Lease()
    {
    }

    /// <summary>The activation this lease was issued to.</summary>
    public Guid ActivationId { get; private set; }

    /// <summary>The control plane's own id for this lease, quoted in support calls.</summary>
    public Guid LeaseReference { get; private set; }

    /// <summary>The signed document, verbatim.</summary>
    public string Document { get; private set; } = string.Empty;

    /// <summary>The module flags in force, as a JSON array.</summary>
    public string Entitlements { get; private set; } = "[]";

    /// <summary>The quantities in force, as a JSON document.</summary>
    public string Limits { get; private set; } = "{}";

    /// <summary>
    /// Where the control plane says this tenant sits on the ladder.
    /// </summary>
    /// <remarks>
    /// Advisory for Path A and authoritative for Path B. The client computes the Path A level itself
    /// from lease age, and the two must agree — there is a test for it — because a store that has lost
    /// its link has to reach the same answer with nobody to ask.
    /// </remarks>
    public EnforcementLevel DeclaredLevel { get; private set; }

    /// <summary>Why, in the control plane's words.</summary>
    public EnforcementReason DeclaredReason { get; private set; }

    /// <summary>When the lease was issued, UTC.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>When it expires, UTC. Expiry alone restricts nobody — see the type's remarks.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>The licence issuance counter this lease was derived from.</summary>
    public long IssuanceCounter { get; private set; }

    /// <summary>What the tenant owes, if anything. Vendor billing, never a tenant ledger amount (ADR-025).</summary>
    public decimal? AmountDueValue { get; private set; }

    /// <summary>The currency of <see cref="AmountDueValue"/>, ISO 4217.</summary>
    public string? AmountDueCurrency { get; private set; }

    /// <summary>Where the customer can pay, right now, from inside the product.</summary>
    public string? PayUrl { get; private set; }

    /// <summary>Where the customer can fix a card that expired. Stays reachable in read-only (Rule 3).</summary>
    public string? UpdatePaymentMethodUrl { get; private set; }

    /// <summary>
    /// When the dunning cycle completed, UTC, or <c>null</c> if it has not.
    /// </summary>
    /// <remarks>
    /// The single field that makes Path B lawful. <c>LICENSING.md</c> §4 and ADR-028 both require a
    /// <em>completed</em> dunning cycle with delivered notifications before anything is restricted; a
    /// lease that declares read-only for non-payment without this is treated as a notice, because a
    /// customer who was never warned has not been through dunning.
    /// </remarks>
    public DateTimeOffset? DunningCompletedAt { get; private set; }

    /// <summary>Until when a vendor-granted write unlock is in force, UTC.</summary>
    public DateTimeOffset? WriteUnlockUntil { get; private set; }

    /// <summary>The vendor's support number, for the licence screen.</summary>
    public string? SupportPhone { get; private set; }

    /// <summary>Messages for the licence screen, as a JSON array.</summary>
    public string Messages { get; private set; } = "[]";

    /// <summary>The entitlement flags, parsed.</summary>
    public IReadOnlySet<string> EntitlementSet() => FingerprintJson.ReadEntitlements(Entitlements);

    /// <summary>The quantities, parsed.</summary>
    public LicenceLimits LimitSet() => FingerprintJson.ReadLimits(Limits);

    /// <summary>The messages, parsed.</summary>
    public IReadOnlyList<string> MessageList() => FingerprintJson.ReadMessages(Messages);

    /// <summary>What is owed, as money with its currency (§7 rule 4).</summary>
    public Money? AmountDue()
        => AmountDueValue is { } amount && !string.IsNullOrWhiteSpace(AmountDueCurrency)
            ? new Money(amount, AmountDueCurrency)
            : null;

    /// <summary>Records a lease whose signature has already been verified.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="storeId">The store this server runs, where it runs one.</param>
    /// <param name="activationId">The activation it was issued to.</param>
    /// <param name="leaseReference">The control plane's id for it.</param>
    /// <param name="document">The signed document.</param>
    /// <param name="entitlements">The module flags in force.</param>
    /// <param name="limits">The quantities in force.</param>
    /// <param name="declaredLevel">Where the control plane says the tenant sits.</param>
    /// <param name="declaredReason">Why.</param>
    /// <param name="issuedAt">When it was issued, UTC.</param>
    /// <param name="expiresAt">When it expires, UTC.</param>
    /// <param name="issuanceCounter">The licence counter it derives from.</param>
    /// <returns>The lease.</returns>
    public static Lease Record(
        Guid tenantId,
        Guid? storeId,
        Guid activationId,
        Guid leaseReference,
        string document,
        IEnumerable<string> entitlements,
        LicenceLimits limits,
        EnforcementLevel declaredLevel,
        EnforcementReason declaredReason,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        long issuanceCounter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(document);
        ArgumentNullException.ThrowIfNull(entitlements);
        ArgumentNullException.ThrowIfNull(limits);

        return new Lease(tenantId, storeId)
        {
            ActivationId = activationId,
            LeaseReference = leaseReference,
            Document = document,
            Entitlements = FingerprintJson.WriteEntitlements(entitlements),
            Limits = FingerprintJson.WriteLimits(limits),
            DeclaredLevel = declaredLevel,
            DeclaredReason = declaredReason,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            IssuanceCounter = issuanceCounter,
        };
    }

    /// <summary>
    /// Attaches what the customer needs in order to fix the problem themselves.
    /// </summary>
    /// <param name="amountDue">What is owed.</param>
    /// <param name="payUrl">Where to pay.</param>
    /// <param name="updatePaymentMethodUrl">Where to fix a card.</param>
    /// <param name="dunningCompletedAt">When dunning completed, if it has.</param>
    /// <param name="writeUnlockUntil">Until when a vendor write unlock is in force.</param>
    /// <param name="supportPhone">The vendor's number.</param>
    /// <param name="messages">Anything else to show on the licence screen.</param>
    /// <returns>This lease, for chaining at the point of construction.</returns>
    /// <remarks>
    /// Set at construction only. <see cref="IImmutableRecord"/> means the persistence layer refuses any
    /// update to a stored lease, so this is a builder step rather than a mutation — the next set of
    /// values arrives as the next lease.
    /// </remarks>
    public Lease WithRecovery(
        Money? amountDue,
        string? payUrl,
        string? updatePaymentMethodUrl,
        DateTimeOffset? dunningCompletedAt,
        DateTimeOffset? writeUnlockUntil,
        string? supportPhone,
        IEnumerable<string>? messages)
    {
        AmountDueValue = amountDue?.Amount;
        AmountDueCurrency = amountDue?.Currency;
        PayUrl = payUrl;
        UpdatePaymentMethodUrl = updatePaymentMethodUrl;
        DunningCompletedAt = dunningCompletedAt;
        WriteUnlockUntil = writeUnlockUntil;
        SupportPhone = supportPhone;
        Messages = FingerprintJson.WriteMessages(messages ?? []);

        return this;
    }
}
