#pragma warning disable CS1591
using VumaRetail.Application.Crm;
using VumaRetail.Domain.Crm;

namespace VumaRetail.Application.Marketing;

public enum MarketingChannel
{
    Email = 1,
    Sms = 2,
    WhatsApp = 3,
    Push = 4,
}

public enum MessageClassification
{
    Marketing = 1,
    Transactional = 2,
}

/// <summary>Evaluates consent immediately before a message leaves the durable delivery queue.</summary>
public sealed class MarketingDeliveryPolicy(IConsentService consents)
{
    public async Task<bool> MaySendAsync(Guid customerId, MarketingChannel channel,
        MessageClassification classification, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consents);
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("Customer is required.", nameof(customerId));
        }
        if (classification == MessageClassification.Transactional)
        {
            return true;
        }
        ConsentType purpose = channel switch
        {
            MarketingChannel.Email => ConsentType.MarketingEmail,
            MarketingChannel.Sms => ConsentType.MarketingSms,
            MarketingChannel.Push => ConsentType.MarketingPush,
            // CRM has no separate WhatsApp purpose yet; fail closed until the consent taxonomy is
            // extended rather than silently treating another channel as email consent.
            MarketingChannel.WhatsApp => throw new InvalidOperationException("WhatsApp marketing consent is not configured."),
            _ => throw new ArgumentOutOfRangeException(nameof(channel)),
        };
        return await consents.IsValidAsync(customerId, purpose, at, cancellationToken).ConfigureAwait(false);
    }
}
