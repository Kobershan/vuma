using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using VumaRetail.Application.Marketing;
using VumaRetail.Domain.Marketing;
using VumaRetail.Infrastructure.Marketing;

namespace VumaRetail.UnitTests.Marketing;

public sealed class HttpMarketingTransportTests
{
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
