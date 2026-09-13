#pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Ecommerce;

[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class PaymentAttempt : Entity
{
    private PaymentAttempt(Guid tenantId, Guid companyId, Guid checkoutId, string eventId,
        string payloadFingerprint, string providerPaymentId, PaymentAttemptStatus status,
        string? providerReference, DateTimeOffset receivedAt)
        : base(tenantId)
    {
        AssignCompany(companyId);
        CheckoutIntentId = checkoutId;
        EventId = eventId.Trim();
        PayloadFingerprint = payloadFingerprint.Trim();
        ProviderPaymentId = providerPaymentId.Trim();
        Status = status;
        ProviderReference = providerReference?.Trim();
        ReceivedAtUtc = receivedAt;
    }

    private PaymentAttempt() { }

    public Guid CheckoutIntentId { get; private set; }
    public string EventId { get; private set; } = string.Empty;
    public string PayloadFingerprint { get; private set; } = string.Empty;
    public string ProviderPaymentId { get; private set; } = string.Empty;
    public PaymentAttemptStatus Status { get; private set; }
    public string? ProviderReference { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public static PaymentAttempt Record(Guid tenantId, Guid companyId, Guid checkoutId, string eventId,
        string payloadFingerprint, string providerPaymentId, PaymentAttemptStatus status,
        string? providerReference, DateTimeOffset receivedAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || checkoutId == Guid.Empty)
            throw new ArgumentException("A payment attempt requires tenant, company and checkout identities.");
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerPaymentId);
        return new PaymentAttempt(tenantId, companyId, checkoutId, eventId, payloadFingerprint,
            providerPaymentId, status, providerReference, receivedAt);
    }

    public static bool IsAllowedTransition(PaymentAttemptStatus current, PaymentAttemptStatus next)
        => current == next || current switch
        {
            PaymentAttemptStatus.Authorised => next is PaymentAttemptStatus.Captured or PaymentAttemptStatus.Failed or PaymentAttemptStatus.Reversed,
            PaymentAttemptStatus.Captured => next == PaymentAttemptStatus.Reversed,
            PaymentAttemptStatus.Failed or PaymentAttemptStatus.Reversed => false,
            _ => false,
        };
}

public enum PaymentAttemptStatus
{
    Authorised = 1,
    Captured = 2,
    Failed = 3,
    Reversed = 4
}
