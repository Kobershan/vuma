using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using VumaRetail.Application.Loyalty;

namespace VumaRetail.Infrastructure.Loyalty;

/// <summary>Options for the Proxima Orbit HTTP client (Stage 20).</summary>
public sealed class OrbitOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Vuma:Loyalty:Orbit";

    /// <summary>Orbit's base URL. Empty means unconfigured — calls fail honest, never silently.</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>API key for Orbit. Stored in configuration, never committed (see .env.example).</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Per-call timeout in seconds. Short: the till never waits on loyalty.</summary>
    public int TimeoutSeconds { get; init; } = 5;

    /// <summary>Whether calls are even attempted. True while no vendor account exists.</summary>
    public bool UseFake { get; init; } = true;
}

/// <summary>
/// The HTTP loyalty engine client for a real Proxima Orbit endpoint (Stage 20). Registered only
/// when <c>Vuma:Loyalty:Orbit:UseFake</c> is false AND a base URL is configured; without
/// credentials it fails honest at startup registration, never silently at the till.
/// </summary>
public sealed class HttpOrbitClient : IOrbitClient
{
    private readonly HttpClient _http;
    private readonly OrbitOptions _options;

    /// <summary>Builds the client.</summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="options">Orbit configuration.</param>
    public HttpOrbitClient(HttpClient http, IOptions<OrbitOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<string> EnsureMemberAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/members:ensure");
        request.Content = JsonContent.Create(new { customerId });
        OrbitMemberResponse response = await SendAsync<OrbitMemberResponse>(request, cancellationToken)
            .ConfigureAwait(false);
        return response.OrbitMemberId;
    }

    /// <inheritdoc />
    public async Task<OrbitEarnResult> EarnAsync(OrbitEarnRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();
        using var message = new HttpRequestMessage(
            HttpMethod.Post, $"v1/members/{request.OrbitMemberId}/earn");
        message.Headers.Add("X-Idempotency-Key", request.IdempotencyKey.ToString("N"));
        message.Content = JsonContent.Create(
            new { request.Points, request.Currency, request.Reference });
        OrbitEarnResponse response = await SendAsync<OrbitEarnResponse>(message, cancellationToken)
            .ConfigureAwait(false);
        return new OrbitEarnResult(true, response.OrbitTransactionId, response.NewBalance, response.TierId);
    }

    /// <inheritdoc />
    public async Task<OrbitRedeemResult> RedeemAsync(OrbitRedeemRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();
        using var message = new HttpRequestMessage(
            HttpMethod.Post, $"v1/members/{request.OrbitMemberId}/redeem");
        message.Headers.Add("X-Idempotency-Key", request.IdempotencyKey.ToString("N"));
        message.Content = JsonContent.Create(new { request.Points, request.Reference });
        OrbitRedeemResponse response = await SendAsync<OrbitRedeemResponse>(message, cancellationToken)
            .ConfigureAwait(false);
        return new OrbitRedeemResult(
            response.Applied, response.RefusedInsufficient, response.OrbitTransactionId,
            response.NewBalance, response.TierId);
    }

    /// <inheritdoc />
    public async Task<OrbitBalanceResult> GetBalanceAsync(string orbitMemberId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var message = new HttpRequestMessage(HttpMethod.Get, $"v1/members/{orbitMemberId}/balance");
        OrbitBalanceResponse response = await SendAsync<OrbitBalanceResponse>(message, cancellationToken)
            .ConfigureAwait(false);
        return new OrbitBalanceResult(response.Balance, response.TierId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TierDefinition>> ListTiersAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var message = new HttpRequestMessage(HttpMethod.Get, "v1/tiers");
        IReadOnlyList<TierDefinition>? tiers = await SendAsync<IReadOnlyList<TierDefinition>>(
                message, cancellationToken)
            .ConfigureAwait(false);
        return tiers ?? [];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RewardDefinition>> ListRewardsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var message = new HttpRequestMessage(HttpMethod.Get, "v1/rewards");
        IReadOnlyList<RewardDefinition>? rewards = await SendAsync<IReadOnlyList<RewardDefinition>>(
                message, cancellationToken)
            .ConfigureAwait(false);
        return rewards ?? [];
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl) || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new OrbitUnavailableException(
                "No Orbit endpoint is configured. Set Vuma:Loyalty:Orbit (see .env.example).");
        }
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new OrbitUnavailableException(
                $"Orbit answered {(int)response.StatusCode}. The request is queued for retry.");
        }

        T? body = await response.Content
            .ReadFromJsonAsync<T>(cancellationToken)
            .ConfigureAwait(false);

        return body ?? throw new OrbitUnavailableException("Orbit answered without a body.");
    }

    private sealed record OrbitMemberResponse(string OrbitMemberId);

    private sealed record OrbitEarnResponse(string OrbitTransactionId, decimal NewBalance, string? TierId);

    private sealed record OrbitRedeemResponse(
        bool Applied, bool RefusedInsufficient, string OrbitTransactionId, decimal NewBalance, string? TierId);

    private sealed record OrbitBalanceResponse(decimal Balance, string? TierId);
}
