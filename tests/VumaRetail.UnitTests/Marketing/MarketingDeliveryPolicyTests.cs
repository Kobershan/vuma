using NSubstitute;
using VumaRetail.Application.Crm;
using VumaRetail.Application.Marketing;
using VumaRetail.Domain.Crm;
using VumaRetail.Domain.Marketing;

namespace VumaRetail.UnitTests.Marketing;

public sealed class MarketingDeliveryPolicyTests
{
    [Fact]
    public async Task Marketing_email_requires_current_email_consent()
    {
        IConsentService consents = Substitute.For<IConsentService>();
        consents.IsValidAsync(Arg.Any<Guid>(), ConsentType.MarketingEmail, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        var policy = new MarketingDeliveryPolicy(consents);

        (await policy.MaySendAsync(Guid.NewGuid(), MarketingChannel.Email, MessageClassification.Marketing, DateTimeOffset.UtcNow)).Should().BeTrue();
        await consents.Received(1).IsValidAsync(Arg.Any<Guid>(), ConsentType.MarketingEmail, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Transactional_messages_do_not_use_marketing_consent()
    {
        IConsentService consents = Substitute.For<IConsentService>();
        var policy = new MarketingDeliveryPolicy(consents);

        (await policy.MaySendAsync(Guid.NewGuid(), MarketingChannel.Email, MessageClassification.Transactional, DateTimeOffset.UtcNow)).Should().BeTrue();
        await consents.DidNotReceiveWithAnyArgs().IsValidAsync(default, default, default, default);
    }

    [Fact]
    public async Task WhatsApp_marketing_fails_closed_until_its_consent_purpose_exists()
    {
        var policy = new MarketingDeliveryPolicy(Substitute.For<IConsentService>());

        Func<Task> action = () => policy.MaySendAsync(Guid.NewGuid(), MarketingChannel.WhatsApp, MessageClassification.Marketing, DateTimeOffset.UtcNow);
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("2026-09-13T19:59:00+00:00", "2026-09-13T19:59:00+00:00")]
    [InlineData("2026-09-13T20:00:00+00:00", "2026-09-14T08:00:00+00:00")]
    [InlineData("2026-09-14T07:59:00+00:00", "2026-09-14T08:00:00+00:00")]
    public void Quiet_hours_defer_to_eight_in_the_recipient_timezone(string scheduled, string expected)
    {
        DateTimeOffset actual = MarketingSendWindow.NextAllowed(DateTimeOffset.Parse(scheduled), TimeZoneInfo.Utc);
        actual.Should().Be(DateTimeOffset.Parse(expected));
    }

    [Fact]
    public void Campaign_scheduling_and_suppression_are_explicit_states()
    {
        var scheduled = DateTimeOffset.UtcNow.AddHours(1);
        var campaign = MarketingCampaign.Create(Guid.NewGuid(), null, Guid.NewGuid(), "September sale", "sale-v1", scheduled);
        campaign.Schedule(DateTimeOffset.UtcNow);
        var message = OutboundMessage.Queue(campaign.TenantId, campaign.StoreId, campaign.CompanyId!.Value,
            campaign.Id, Guid.NewGuid(), "campaign-1-recipient-1", scheduled);
        message.Suppress();
        campaign.Status.Should().Be(MarketingCampaignStatus.Scheduled);
        message.Status.Should().Be(OutboundMessageStatus.Suppressed);
    }

    [Fact]
    public void Campaign_scheduling_rejects_past_delivery_time()
    {
        var campaign = MarketingCampaign.Create(Guid.NewGuid(), null, Guid.NewGuid(), "Old", "old-v1", DateTimeOffset.UtcNow.AddMinutes(-1));
        var action = () => campaign.Schedule(DateTimeOffset.UtcNow);
        action.Should().Throw<InvalidOperationException>();
    }
}
