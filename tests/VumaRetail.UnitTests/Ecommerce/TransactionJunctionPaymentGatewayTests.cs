using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using VumaRetail.Application.Ecommerce;
using VumaRetail.Infrastructure.Ecommerce;

namespace VumaRetail.UnitTests.Ecommerce;

public sealed class TransactionJunctionPaymentGatewayTests
{
    private static readonly PaymentAuthorizationRequest Authorization = new(
        Guid.NewGuid(), "VUMA-CHK-1", 125.50m, "ZAR",
        new Uri("https://shop.example/paid"), new Uri("https://shop.example/cancel"),
        new Uri("https://shop.example/api/payment-notifications"));

    [Fact]
    public async Task Hosted_payment_page_returns_redirect_without_accepting_card_data()
    {
        var options = Options.Create(new TransactionJunctionOptions
        {
            Mode = TransactionJunctionMode.HostedPaymentPage,
            MerchantId = "merchant-1",
            HostedPaymentPageUrl = "https://tj.example/pay"
        });
        var gateway = new TransactionJunctionPaymentGateway(new HttpClient(), options);

        PaymentGatewayAuthorization result = await gateway.AuthorizeAsync(Authorization);

        result.Status.Should().Be("RedirectRequired");
        result.RedirectUrl.Should().NotBeNull();
        result.RedirectUrl!.ToString().Should().Contain("merchantId=merchant-1");
        result.RedirectUrl.ToString().Should().Contain("amount=125.50");
        result.RedirectUrl.ToString().Should().NotContain("card");
    }

    [Fact]
    public async Task Hosted_payment_page_rejects_server_side_capture()
    {
        var gateway = new TransactionJunctionPaymentGateway(new HttpClient(), Options.Create(new TransactionJunctionOptions
        {
            Mode = TransactionJunctionMode.HostedPaymentPage,
            HostedPaymentPageUrl = "https://tj.example/pay"
        }));

        Func<Task> action = () => gateway.CaptureAsync(new PaymentGatewayOperation(
            "provider-1", "VUMA-CHK-1", 125.50m, "ZAR", "capture-1"));

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Capture, void and refund require the configured Transaction Junction Direct API.");
    }

    [Fact]
    public async Task Direct_api_sends_idempotency_key_and_never_sends_card_fields()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"providerPaymentId\":\"tj-1\",\"status\":\"Captured\"}", Encoding.UTF8, "application/json")
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://tj.example") };
        var gateway = new TransactionJunctionPaymentGateway(client, Options.Create(new TransactionJunctionOptions
        {
            Mode = TransactionJunctionMode.DirectApi,
            DirectCapturePath = "/payments/capture"
        }));

        PaymentGatewayResult result = await gateway.CaptureAsync(new PaymentGatewayOperation(
            "provider-1", "VUMA-CHK-1", 125.50m, "ZAR", "capture-1"));

        result.ProviderPaymentId.Should().Be("tj-1");
        handler.Request!.Headers.GetValues("Idempotency-Key").Single().Should().Be("capture-1");
        handler.Body.Should().NotContain("card");
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
