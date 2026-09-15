using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Crm;
using VumaRetail.Application.Marketing;
using VumaRetail.Domain.Crm;
using VumaRetail.Domain.Marketing;

namespace VumaRetail.UnitTests.Marketing;

public sealed class MarketingDeliveryWorkerTests
{
    [Fact]
    public async Task Worker_dispatches_only_the_bounded_due_queue()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid companyId = Guid.NewGuid();
        MarketingCampaign campaign = MarketingCampaign.Create(Guid.NewGuid(), null, companyId, "Sale", "sale-v1", now);
        campaign.Schedule(now);
        OutboundMessage first = OutboundMessage.Queue(campaign.TenantId, null, companyId, campaign.Id, Guid.NewGuid(), "worker-1", now);
        OutboundMessage second = OutboundMessage.Queue(campaign.TenantId, null, companyId, campaign.Id, Guid.NewGuid(), "worker-2", now);

        IOutboundMessageRepository messages = Substitute.For<IOutboundMessageRepository>();
        messages.ListQueuedAsync(companyId, now, true, 1, Arg.Any<CancellationToken>())
            .Returns([first]);
        messages.FindAsync(first.Id, Arg.Any<CancellationToken>()).Returns(first);
        IMarketingCampaignRepository campaigns = Substitute.For<IMarketingCampaignRepository>();
        campaigns.FindAsync(campaign.Id, Arg.Any<CancellationToken>()).Returns(campaign);
        IConsentService consents = Substitute.For<IConsentService>();
        consents.IsValidAsync(first.CustomerId, ConsentType.MarketingEmail, now, Arg.Any<CancellationToken>()).Returns(true);
        IMarketingTransport transport = Substitute.For<IMarketingTransport>();
        transport.SendAsync(first, campaign, Arg.Any<CancellationToken>())
            .Returns(new MarketingTransportResult("evt-worker-1", "fp-worker-1", true));
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);

        int processed = await new MarketingDeliveryWorker(
            messages,
            new MarketingDeliveryService(campaigns, messages, new MarketingDeliveryPolicy(consents), transport, clock),
            clock).DispatchDueAsync(companyId, limit: 1);

        processed.Should().Be(1);
        first.Status.Should().Be(OutboundMessageStatus.Sent);
        second.Status.Should().Be(OutboundMessageStatus.Queued);
        await messages.Received(1).ListQueuedAsync(companyId, now, true, 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Worker_rejects_an_unbounded_or_missing_company_scope()
    {
        IOutboundMessageRepository messages = Substitute.For<IOutboundMessageRepository>();
        MarketingDeliveryService delivery = new(
            Substitute.For<IMarketingCampaignRepository>(), messages,
            new MarketingDeliveryPolicy(Substitute.For<IConsentService>()),
            Substitute.For<IMarketingTransport>(), Substitute.For<IClock>());
        IClock clock = Substitute.For<IClock>();

        await FluentActions.Invoking(() => new MarketingDeliveryWorker(messages, delivery, clock)
                .DispatchDueAsync(Guid.Empty))
            .Should().ThrowAsync<ArgumentException>();
    }
}
