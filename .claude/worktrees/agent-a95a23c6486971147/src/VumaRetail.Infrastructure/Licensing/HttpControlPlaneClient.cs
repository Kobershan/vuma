using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions.Licensing;

namespace VumaRetail.Infrastructure.Licensing;

/// <summary>
/// The vendor's device API over HTTP (<c>docs/API_CONTROL_PLANE.md</c> §2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything that is not a well-formed answer is
/// <see cref="ControlPlaneUnreachableException"/>.</b> A timeout, a DNS failure, a TLS failure, a 500,
/// a 502 from a proxy, a body that will not parse — all of them mean the same thing to a store, which
/// is "we do not know", and all of them therefore mean the Path A tolerance window and nothing else.
/// This is where ADR-028's fourth rule is actually implemented: one bad vendor deployment must not
/// look like every customer's subscription lapsing at once.
/// </para>
/// <para>
/// Only the documented refusals — 402, 409, 422 — become a
/// <see cref="ControlPlaneRefusedException"/>, because only those are the vendor saying something.
/// </para>
/// <para>
/// Transport security is mTLS with a per-node client certificate, configured on the handler by the
/// host. The certificate arrives with the activation response; wiring it into Kestrel and into this
/// client's handler is Stage 31's installer work, and is listed in <c>PROGRESS.md</c> as such.
/// </para>
/// </remarks>
/// <param name="client">The configured <c>HttpClient</c>, with base address and timeout.</param>
/// <param name="logger">Where a failed call is noted at debug.</param>
public sealed class HttpControlPlaneClient(HttpClient client, ILogger<HttpControlPlaneClient> logger)
    : IControlPlaneClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public Task<ActivationGrant> ActivateAsync(
        ActivationRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync<ActivationRequest, ActivationGrant>("activations", request, cancellationToken);

    /// <inheritdoc />
    public Task<ActivationGrant> RebindAsync(RebindRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PostAsync<RebindRequest, ActivationGrant>(
            $"activations/{request.ActivationReference}/rebind",
            request,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<LeaseGrant> RefreshLeaseAsync(LeaseRequest request, CancellationToken cancellationToken = default)
        => PostAsync<LeaseRequest, LeaseGrant>("lease", request, cancellationToken);

    /// <inheritdoc />
    public Task<HeartbeatAcknowledgement> HeartbeatAsync(
        HeartbeatRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync<HeartbeatRequest, HeartbeatAcknowledgement>("heartbeat", request, cancellationToken);

    /// <inheritdoc />
    public async Task SendMeteringAsync(
        string nodeId,
        DateOnly period,
        string payload,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "metering")
        {
            // The payload is already serialised and already whitelisted. Re-serialising it through a
            // DTO here would be a second place a field could be added, which is the one thing R10's
            // enforcement cannot afford.
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
        };

        request.Headers.Add("X-Vuma-Node", nodeId);
        request.Headers.Add("X-Vuma-Period", period.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        using (response)
        {
            await ThrowForStatusAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: Json),
        };

        HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        using (response)
        {
            await ThrowForStatusAsync(response, cancellationToken).ConfigureAwait(false);

            try
            {
                return await response.Content
                    .ReadFromJsonAsync<TResponse>(Json, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new ControlPlaneUnreachableException(
                        "The licence service answered with an empty body.");
            }
            catch (JsonException failure)
            {
                // Garbage is unreachable. A body that will not parse tells a store nothing about its
                // subscription, and guessing would be guessing in the direction that stops a shop
                // trading.
                throw new ControlPlaneUnreachableException(
                    "The licence service answered with something unreadable.",
                    failure);
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException failure)
        {
            logger.LogDebug(failure, "The licence service could not be reached.");

            throw new ControlPlaneUnreachableException("The licence service could not be reached.", failure);
        }
        catch (TaskCanceledException failure) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(failure, "The licence service timed out.");

            throw new ControlPlaneUnreachableException("The licence service timed out.", failure);
        }
    }

    private static async Task ThrowForStatusAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ControlPlaneRefusal? refusal = (int)response.StatusCode switch
        {
            402 => ControlPlaneRefusal.SubscriptionNotActive,
            404 => ControlPlaneRefusal.ActivationUnknown,
            409 => ControlPlaneRefusal.LicenceAlreadyActivated,
            422 => ControlPlaneRefusal.LicenceKeyInvalid,
            _ => null,
        };

        if (refusal is null)
        {
            // Every other status — 500, 502, 503, 429, an HTML error page from a captive portal — is
            // "we do not know".
            throw new ControlPlaneUnreachableException(
                $"The licence service answered {(int)response.StatusCode}.");
        }

        string detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        throw new ControlPlaneRefusedException(
            refusal.Value,
            string.IsNullOrWhiteSpace(detail)
                ? "The licence service refused this request."
                : detail);
    }
}
