using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using VumaRetail.Application.Marketing;
using VumaRetail.Domain.Marketing;

namespace VumaRetail.Infrastructure.Marketing;

/// <summary>Configuration for the tenant's approved marketing provider gateway.</summary>
public sealed class MarketingTransportOptions
{
    public const string SectionName = "Vuma:Marketing";

    /// <summary>Provider endpoint receiving one durable outbound-message delivery request.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Optional bearer credential supplied by deployment configuration.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Sends a provider-neutral delivery request over HTTPS. Provider-specific gateways can expose this
/// small contract while Vuma retains the durable message identity and replay boundary. No recipient
/// address or message body is accepted from the queue row; the provider resolves the customer identity
/// within its own approved integration boundary.
/// </summary>
public sealed class HttpMarketingTransport(
    HttpClient client,
    IOptions<MarketingTransportOptions> options) : IMarketingTransport
{
    public async Task<MarketingTransportResult> SendAsync(
        OutboundMessage message,
        MarketingCampaign campaign,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(campaign);

        MarketingTransportOptions settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            throw new InvalidOperationException("No marketing provider transport is configured.");
        }

        using HttpRequestMessage request = new(HttpMethod.Post, settings.Endpoint);
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", settings.ApiKey);
        }

        MarketingDeliveryRequest payload = new(
            message.Id, message.TenantId, message.CompanyId!.Value, message.CustomerId,
            campaign.TemplateId, message.IdempotencyKey, message.Channel, message.Classification);
        string serialized = System.Text.Json.JsonSerializer.Serialize(payload);
        string fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
        request.Content = JsonContent.Create(payload);

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Marketing provider returned {(int)response.StatusCode}.");
        }

        ProviderDeliveryResponse? result = await response.Content
            .ReadFromJsonAsync<ProviderDeliveryResponse>(cancellationToken)
            .ConfigureAwait(false);
        if (result is null || string.IsNullOrWhiteSpace(result.ProviderEventId))
        {
            throw new InvalidOperationException("Marketing provider returned no event identity.");
        }

        return new MarketingTransportResult(
            result.ProviderEventId.Trim(),
            string.IsNullOrWhiteSpace(result.PayloadFingerprint) ? fingerprint : result.PayloadFingerprint.Trim(),
            result.Delivered);
    }

    private sealed record MarketingDeliveryRequest(
        Guid MessageId,
        Guid TenantId,
        Guid CompanyId,
        Guid CustomerId,
        string TemplateId,
        string IdempotencyKey,
        MarketingMessageChannel Channel,
        MarketingMessageClassification Classification);

    private sealed record ProviderDeliveryResponse(
        [property: JsonPropertyName("providerEventId")] string ProviderEventId,
        [property: JsonPropertyName("delivered")] bool Delivered,
        [property: JsonPropertyName("payloadFingerprint")] string? PayloadFingerprint);
}
