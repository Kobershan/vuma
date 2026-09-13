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
            MarketingChannel.WhatsApp => ConsentType.MarketingWhatsApp,
            _ => throw new ArgumentOutOfRangeException(nameof(channel)),
        };
        return await consents.IsValidAsync(customerId, purpose, at, cancellationToken).ConfigureAwait(false);
    }
}

public static class MarketingSendWindow
{
    public static DateTimeOffset NextAllowed(DateTimeOffset scheduledAtUtc, TimeZoneInfo recipientZone)
    {
        ArgumentNullException.ThrowIfNull(recipientZone);
        DateTime local = TimeZoneInfo.ConvertTime(scheduledAtUtc, recipientZone).DateTime;
        if (local.TimeOfDay >= new TimeSpan(20, 0, 0))
        {
            return ToUtc(recipientZone, local.Date.AddDays(1).AddHours(8));
        }
        if (local.TimeOfDay < new TimeSpan(8, 0, 0))
        {
            return ToUtc(recipientZone, local.Date.AddHours(8));
        }
        return scheduledAtUtc.ToUniversalTime();
    }

    private static DateTimeOffset ToUtc(TimeZoneInfo zone, DateTime local)
        => new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone));
}
