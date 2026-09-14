#pragma warning disable CS1591
using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using VumaRetail.Application.Ecommerce;

namespace VumaRetail.Infrastructure.Ecommerce;

/// <summary>Transaction Junction IMBEKO gateway adapter.</summary>
/// <remarks>Hosted Payment Page is the default and never accepts card data.</remarks>
public sealed class TransactionJunctionPaymentGateway(HttpClient httpClient, IOptions<TransactionJunctionOptions> options) : IPaymentGateway
{
    private readonly TransactionJunctionOptions _options = options.Value;

    public Task<PaymentGatewayAuthorization> AuthorizeAsync(PaymentAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request.Amount, request.Currency, request.MerchantReference);
        return _options.Mode switch
        {
            TransactionJunctionMode.HostedPaymentPage => Task.FromResult(BuildHostedAuthorization(request)),
            TransactionJunctionMode.DirectApi => SendDirectAuthorizeAsync(request, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported Transaction Junction mode '{_options.Mode}'.")
        };
    }

    public Task<PaymentGatewayResult> CaptureAsync(PaymentGatewayOperation request, CancellationToken cancellationToken = default)
        => SendOperationAsync("capture", request, cancellationToken);

    public Task<PaymentGatewayResult> VoidAsync(PaymentGatewayOperation request, CancellationToken cancellationToken = default)
        => SendOperationAsync("void", request, cancellationToken);

    public Task<PaymentGatewayResult> RefundAsync(PaymentGatewayOperation request, CancellationToken cancellationToken = default)
        => SendOperationAsync("refund", request, cancellationToken);

    private PaymentGatewayAuthorization BuildHostedAuthorization(PaymentAuthorizationRequest request)
    {
        if (!Uri.TryCreate(_options.HostedPaymentPageUrl, UriKind.Absolute, out Uri? endpoint))
            throw new InvalidOperationException("Vuma:Ecommerce:PaymentGateway:HostedPaymentPageUrl must be an absolute URL.");
        string separator = string.IsNullOrEmpty(endpoint.Query) ? "?" : "&";
        string query = string.Join("&", new Dictionary<string, string>
        {
            ["merchantId"] = _options.MerchantId,
            ["reference"] = request.MerchantReference,
            ["amount"] = request.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            ["currency"] = request.Currency.Trim().ToUpperInvariant(),
            ["returnUrl"] = request.ReturnUrl.ToString(),
            ["cancelUrl"] = request.CancelUrl.ToString(),
            ["notificationUrl"] = request.NotificationUrl.ToString()
        }.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new PaymentGatewayAuthorization(request.MerchantReference,
            new Uri(endpoint.AbsoluteUri + separator + query), "RedirectRequired");
    }

    private async Task<PaymentGatewayAuthorization> SendDirectAuthorizeAsync(PaymentAuthorizationRequest request, CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, RequiredEndpoint("authorize"));
        message.Headers.Add("Idempotency-Key", request.MerchantReference);
        message.Content = JsonContent.Create(new
        {
            merchantReference = request.MerchantReference,
            amount = request.Amount,
            currency = request.Currency.Trim().ToUpperInvariant(),
            returnUrl = request.ReturnUrl,
            cancelUrl = request.CancelUrl,
            notificationUrl = request.NotificationUrl
        });
        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureSuccess(response).ConfigureAwait(false);
        GatewayResponse? body = await response.Content.ReadFromJsonAsync<GatewayResponse>(cancellationToken).ConfigureAwait(false);
        return body is null || string.IsNullOrWhiteSpace(body.ProviderPaymentId)
            ? throw new InvalidOperationException("Transaction Junction returned no provider payment id.")
            : new PaymentGatewayAuthorization(body.ProviderPaymentId, null, body.Status ?? "Authorised", body.ProviderReference);
    }

    private async Task<PaymentGatewayResult> SendOperationAsync(string operation, PaymentGatewayOperation request, CancellationToken cancellationToken)
    {
        Validate(request.Amount, request.Currency, request.MerchantReference);
        if (_options.Mode == TransactionJunctionMode.HostedPaymentPage)
            throw new InvalidOperationException("Capture, void and refund require the configured Transaction Junction Direct API.");
        using HttpRequestMessage message = new(HttpMethod.Post, RequiredEndpoint(operation));
        message.Headers.Add("Idempotency-Key", request.IdempotencyKey);
        message.Content = JsonContent.Create(new
        {
            providerPaymentId = request.ProviderPaymentId,
            merchantReference = request.MerchantReference,
            amount = request.Amount,
            currency = request.Currency.Trim().ToUpperInvariant()
        });
        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureSuccess(response).ConfigureAwait(false);
        GatewayResponse? body = await response.Content.ReadFromJsonAsync<GatewayResponse>(cancellationToken).ConfigureAwait(false);
        return body is null || string.IsNullOrWhiteSpace(body.ProviderPaymentId)
            ? throw new InvalidOperationException("Transaction Junction returned no provider payment id.")
            : new PaymentGatewayResult(body.ProviderPaymentId, body.Status ?? operation, body.ProviderReference);
    }

    private Uri RequiredEndpoint(string operation)
    {
        string? path = operation switch
        {
            "authorize" => _options.DirectAuthorizePath,
            "capture" => _options.DirectCapturePath,
            "void" => _options.DirectVoidPath,
            "refund" => _options.DirectRefundPath,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException($"Transaction Junction endpoint for '{operation}' is not configured.");
        return new Uri(httpClient.BaseAddress ?? throw new InvalidOperationException("Transaction Junction API base address is not configured."), path);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new HttpRequestException($"Transaction Junction request failed with {(int)response.StatusCode}: {detail}");
    }

    private static void Validate(decimal amount, string currency, string reference)
    {
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3) throw new ArgumentException("Currency must be an ISO 4217 code.", nameof(currency));
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
    }

    private sealed record GatewayResponse(string? ProviderPaymentId, string? Status, string? ProviderReference);
}

public enum TransactionJunctionMode { HostedPaymentPage = 1, DirectApi = 2 }

public sealed class TransactionJunctionOptions
{
    public const string SectionName = "Vuma:Ecommerce:PaymentGateway";
    public TransactionJunctionMode Mode { get; set; } = TransactionJunctionMode.HostedPaymentPage;
    public string MerchantId { get; set; } = string.Empty;
    public string HostedPaymentPageUrl { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string DirectAuthorizePath { get; set; } = "/payments/authorize";
    public string DirectCapturePath { get; set; } = "/payments/capture";
    public string DirectVoidPath { get; set; } = "/payments/void";
    public string DirectRefundPath { get; set; } = "/payments/refund";
}
