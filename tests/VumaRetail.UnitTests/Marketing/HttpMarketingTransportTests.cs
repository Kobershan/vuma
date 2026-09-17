using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Crm;
using VumaRetail.Application.Marketing;
using VumaRetail.Domain.Crm;
using VumaRetail.Domain.Marketing;
using VumaRetail.Infrastructure.Marketing;

namespace VumaRetail.UnitTests.Marketing;

public sealed class HttpMarketingTransportTests
{
    [Fact]
    public async Task Delivery_service_completes_a_due_queue_row_through_the_configured_https_transport()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid companyId = Guid.NewGuid();
        Guid customerId = Guid.NewGuid();
        MarketingCampaign campaign = MarketingCampaign.Create(Guid.NewGuid(), null, companyId,
            "Sale", "sale-v1", now);
        campaign.Schedule(now);
        OutboundMessage message = OutboundMessage.Queue(campaign.TenantId, null, companyId,
            campaign.Id, customerId, "sale-v1-customer-v1", now, MarketingMessageChannel.Email);

        IMarketingCampaignRepository campaigns = Substitute.For<IMarketingCampaignRepository>();
        campaigns.FindAsync(campaign.Id, Arg.Any<CancellationToken>()).Returns(campaign);
        IOutboundMessageRepository messages = Substitute.For<IOutboundMessageRepository>();
        messages.FindAsync(message.Id, Arg.Any<CancellationToken>()).Returns(message);
        IConsentService consent = Substitute.For<IConsentService>();
        consent.IsValidAsync(customerId, ConsentType.MarketingEmail, now, Arg.Any<CancellationToken>()).Returns(true);
        using HttpClient client = new(new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { providerEventId = "provider-e2e-1", delivered = true })
        }));
        IMarketingTransport transport = new HttpMarketingTransport(client, Options.Create(
            new MarketingTransportOptions { Endpoint = "https://provider.example/messages" }));
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);

        MarketingDispatchOutcome outcome = await new MarketingDeliveryService(
            campaigns, messages, new MarketingDeliveryPolicy(consent), transport, clock)
            .DispatchAsync(message.Id);

        outcome.Should().Be(MarketingDispatchOutcome.Delivered);
        message.Status.Should().Be(OutboundMessageStatus.Sent);
        message.ProviderEventId.Should().Be("provider-e2e-1");
    }

    [Fact]
    public async Task Sends_durable_identity_and_returns_provider_result()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        using HttpClient client = new(new CapturingHandler(request =>
        {
            captured = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { providerEventId = "provider-1", delivered = true })
            };
        }));
        var transport = new HttpMarketingTransport(client, Options.Create(new MarketingTransportOptions
        {
            Endpoint = "https://provider.example/messages",
            ApiKey = "secret-from-test-configuration"
        }));
        MarketingCampaign campaign = MarketingCampaign.Create(Guid.NewGuid(), null, Guid.NewGuid(),
            "Sale", "sale-v1", DateTimeOffset.UtcNow.AddHours(1));
        OutboundMessage message = OutboundMessage.Queue(campaign.TenantId, null, campaign.CompanyId!.Value,
            campaign.Id, Guid.NewGuid(), "campaign-step-recipient-v1", campaign.ScheduledAt,
            MarketingMessageChannel.WhatsApp, MarketingMessageClassification.Marketing);

        MarketingTransportResult result = await transport.SendAsync(message, campaign);

        result.ProviderEventId.Should().Be("provider-1");
        result.Delivered.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization.Parameter.Should().Be("secret-from-test-configuration");
        capturedBody.Should().Contain(message.Id.ToString());
        capturedBody.Should().Contain(message.IdempotencyKey);
    }

    [Fact]
    public async Task Fails_closed_when_provider_endpoint_is_not_configured()
    {
        using HttpClient client = new(new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var transport = new HttpMarketingTransport(client, Options.Create(new MarketingTransportOptions()));
        MarketingCampaign campaign = MarketingCampaign.Create(Guid.NewGuid(), null, Guid.NewGuid(),
            "Sale", "sale-v1", DateTimeOffset.UtcNow.AddHours(1));
        OutboundMessage message = OutboundMessage.Queue(campaign.TenantId, null, campaign.CompanyId!.Value,
            campaign.Id, Guid.NewGuid(), "missing-provider", campaign.ScheduledAt);

        await FluentAssertions.FluentActions.Invoking(() => transport.SendAsync(message, campaign))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No marketing provider transport is configured.");
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
