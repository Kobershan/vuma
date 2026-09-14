using NSubstitute;
using VumaRetail.Application.Crm;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
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
    public async Task WhatsApp_marketing_requires_explicit_whatsapp_consent()
    {
        IConsentService consents = Substitute.For<IConsentService>();
        consents.IsValidAsync(Arg.Any<Guid>(), ConsentType.MarketingWhatsApp, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        var policy = new MarketingDeliveryPolicy(consents);

        (await policy.MaySendAsync(Guid.NewGuid(), MarketingChannel.WhatsApp, MessageClassification.Marketing, DateTimeOffset.UtcNow)).Should().BeTrue();
        await consents.Received(1).IsValidAsync(Arg.Any<Guid>(), ConsentType.MarketingWhatsApp, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
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

    [Fact]
    public async Task Queue_handler_replays_identical_idempotency_key_without_adding_a_message()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var existing = OutboundMessage.Queue(tenantId, null, companyId, Guid.NewGuid(), Guid.NewGuid(), "same-key", DateTimeOffset.UtcNow.AddHours(1));
        var messages = Substitute.For<IOutboundMessageRepository>();
        messages.FindByIdempotencyKeyAsync("same-key", Arg.Any<CancellationToken>()).Returns(existing);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);

        var result = await new QueueOutboundMessageCommandHandler(messages, tenant, company)
            .HandleAsync(new QueueOutboundMessageCommand(companyId, null, existing.CampaignId, existing.CustomerId, "same-key", existing.ScheduledAt));

        result.Should().Be(existing.Id);
        messages.DidNotReceive().Add(Arg.Any<OutboundMessage>());
    }

    [Fact]
    public async Task Campaign_create_handler_rejects_a_non_active_company()
    {
        var campaigns = Substitute.For<IMarketingCampaignRepository>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(Guid.NewGuid());

        await FluentAssertions.FluentActions.Invoking(() => new CreateMarketingCampaignCommandHandler(campaigns, tenant, company)
            .HandleAsync(new CreateMarketingCampaignCommand(Guid.NewGuid(), null, "Sale", "sale-v1", DateTimeOffset.UtcNow.AddHours(1))))
            .Should().ThrowAsync<InvalidOperationException>();
        campaigns.DidNotReceive().Add(Arg.Any<MarketingCampaign>());
    }

    [Fact]
    public async Task Queue_handler_rejects_idempotency_key_owned_by_another_company()
    {
        var tenantId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var activeCompanyId = Guid.NewGuid();
        var existing = OutboundMessage.Queue(tenantId, null, otherCompanyId, Guid.NewGuid(), Guid.NewGuid(), "shared-key", DateTimeOffset.UtcNow.AddHours(1));
        var messages = Substitute.For<IOutboundMessageRepository>();
        messages.FindByIdempotencyKeyAsync("shared-key", Arg.Any<CancellationToken>()).Returns(existing);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(activeCompanyId);

        await FluentActions.Invoking(() => new QueueOutboundMessageCommandHandler(messages, tenant, company)
            .HandleAsync(new QueueOutboundMessageCommand(activeCompanyId, null, existing.CampaignId, existing.CustomerId,
                "shared-key", existing.ScheduledAt)))
            .Should().ThrowAsync<InvalidOperationException>();
        messages.DidNotReceive().Add(Arg.Any<OutboundMessage>());
    }

    [Fact]
    public async Task Suppress_handler_rejects_a_message_from_another_company()
    {
        var message = OutboundMessage.Queue(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "message-1", DateTimeOffset.UtcNow.AddHours(1));
        var messages = Substitute.For<IOutboundMessageRepository>();
        messages.FindAsync(message.Id, Arg.Any<CancellationToken>()).Returns(message);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(Guid.NewGuid());
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(message.TenantId);

        await FluentActions.Invoking(() => new SuppressOutboundMessageCommandHandler(messages, company, tenant)
            .HandleAsync(new SuppressOutboundMessageCommand(message.Id)))
            .Should().ThrowAsync<InvalidOperationException>();
        message.Status.Should().Be(OutboundMessageStatus.Queued);
    }

    [Fact]
    public async Task Sent_handler_rejects_a_message_from_another_tenant_before_company_lookup()
    {
        var message = OutboundMessage.Queue(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "message-tenant", DateTimeOffset.UtcNow.AddHours(1));
        var messages = Substitute.For<IOutboundMessageRepository>();
        messages.FindAsync(message.Id, Arg.Any<CancellationToken>()).Returns(message);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(message.CompanyId!.Value);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());

        await FluentActions.Invoking(() => new MarkOutboundMessageSentCommandHandler(messages, company, tenant)
            .HandleAsync(new MarkOutboundMessageSentCommand(message.Id)))
            .Should().ThrowAsync<InvalidOperationException>();
        message.Status.Should().Be(OutboundMessageStatus.Queued);
    }

    [Fact]
    public void Provider_result_replay_is_idempotent_but_changed_content_is_rejected()
    {
        var message = OutboundMessage.Queue(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "provider-replay", DateTimeOffset.UtcNow.AddHours(1));

        message.ApplyProviderResult("evt-1", "sha256:a", delivered: true);
        message.ApplyProviderResult("evt-1", "sha256:a", delivered: true);
        var action = () => message.ApplyProviderResult("evt-1", "sha256:b", delivered: false);

        message.Status.Should().Be(OutboundMessageStatus.Sent);
        action.Should().Throw<InvalidOperationException>();
    }
}
