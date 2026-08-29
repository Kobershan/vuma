using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Contracts.Sync;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;

namespace VumaRetail.Infrastructure.Sync;

/// <summary>Where the peer is and how this node authenticates to it.</summary>
public sealed class SyncPeerOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Vuma:Sync:Peer";

    /// <summary>The peer's node id, for the cursor.</summary>
    public string NodeId { get; set; } = "cloud";

    /// <summary>The peer's base address, for example <c>https://cloud.vuma.example</c>.</summary>
    public string BaseAddress { get; set; } = string.Empty;

    /// <summary>
    /// The bearer token this node presents. Must hold <c>sync.batch.push</c> and nothing else.
    /// </summary>
    /// <remarks>
    /// A service credential rather than a person's. <c>sync.batch.push</c> writes any replicated row
    /// under any conflict policy without passing a business rule, so it belongs to a node and to
    /// nobody who signs in.
    /// </remarks>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>How long one batch may take before it counts as unreachable.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Ships batches to a peer over the versioned REST API.
/// </summary>
/// <remarks>
/// <para>
/// The same endpoint a terminal uses to reach the store server and a store server uses to reach the
/// cloud. One protocol, three tiers — which is what ADR-003's three-tier sync needs Stage 04 to
/// "handle generically".
/// </para>
/// <para>
/// <b>Every failure becomes a <see cref="SyncTransportException"/>.</b> That is not exception
/// laundering; it is the contract the dispatcher is written against. A DNS failure, a 502 from a
/// proxy, a timeout and a 500 from the peer are all the same thing to a store server — try again
/// later — and R1 depends on none of them reaching anywhere near a till.
/// </para>
/// </remarks>
/// <param name="client">The configured HTTP client.</param>
/// <param name="options">Where the peer is.</param>
public sealed class HttpSyncTransport(HttpClient client, SyncPeerOptions options) : ISyncTransport
{
    /// <summary>The route a batch is posted to, on both the store server and the cloud.</summary>
    public const string BatchRoute = "api/v1/sync/batches";

    /// <inheritdoc />
    public string PeerNode => options.NodeId;

    /// <inheritdoc />
    public async Task<SyncAcknowledgement> SendAsync(
        SyncBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        HttpResponseMessage response;

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, BatchRoute)
            {
                Content = JsonContent.Create(ToRequest(batch)),
            };

            if (!string.IsNullOrWhiteSpace(options.AccessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
            }

            response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException unreachable)
        {
            throw new SyncTransportException($"{options.NodeId} is unreachable.", unreachable);
        }
        catch (TaskCanceledException timeout) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SyncTransportException($"{options.NodeId} did not answer within {options.Timeout}.", timeout);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new SyncTransportException(
                    $"{options.NodeId} answered {(int)response.StatusCode} "
                    + $"{response.StatusCode}{await DescribeAsync(response).ConfigureAwait(false)}");
            }

            SyncBatchResponse? acknowledgement = await response.Content
                .ReadFromJsonAsync<SyncBatchResponse>(cancellationToken)
                .ConfigureAwait(false);

            return acknowledgement is null
                ? throw new SyncTransportException($"{options.NodeId} answered with an empty body.")
                : FromResponse(acknowledgement);
        }
    }

    private static SyncBatchRequest ToRequest(SyncBatch batch)
        => new(
            batch.SourceNode,
            batch.SourceKind.ToString(),
            batch.TenantId,
            batch.StoreId,
            [.. batch.Operations.Select(operation => new SyncOperationDto(
                operation.OperationId,
                operation.EntityType,
                operation.EntityId,
                operation.Operation.ToString(),
                operation.Stamp.ToString(),
                operation.Payload,
                operation.OccurredAt)
            { CompanyId = operation.CompanyId })])
        { CompanyId = batch.CompanyId };

    private static SyncAcknowledgement FromResponse(SyncBatchResponse response)
        => new(
            response.ReceiverNode,
            [.. response.Results.Select(result => new SyncOperationResult(
                result.OperationId,
                Enum.TryParse(result.Outcome, out InboxOutcome outcome) ? outcome : InboxOutcome.Rejected,
                result.ConflictId,
                result.Detail))],
            HlcStamp.Parse(response.AcknowledgedStamp));

    /// <summary>
    /// A short description of a failure response, for the operator.
    /// </summary>
    /// <remarks>
    /// Truncated, and only for the statuses where the body is a <c>ProblemDetails</c> worth reading.
    /// The error is stored on the outbox row and shown in the back office, and a 400-kilobyte HTML
    /// error page from a captive-portal WiFi login is not what anybody needs to see there.
    /// </remarks>
    private static async Task<string> DescribeAsync(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.BadRequest
            or HttpStatusCode.Forbidden
            or HttpStatusCode.Conflict
            or HttpStatusCode.UnprocessableEntity))
        {
            return string.Empty;
        }

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return body.Length == 0
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $": {body[..Math.Min(body.Length, MaxDescribedBodyLength)]}");
    }

    private const int MaxDescribedBodyLength = 512;
}

/// <summary>
/// A transport that hands a batch to a receiver in the same process.
/// </summary>
/// <remarks>
/// Not only a test double, though it is what makes the whole protocol testable without a second host.
/// It is also the shape a terminal uses when its store server is on the same machine, which is the
/// single-till shop — the most common Vuma installation there will ever be.
/// </remarks>
/// <param name="peerNode">The peer's node id.</param>
/// <param name="receive">Hands a batch to the receiver.</param>
public sealed class InProcessSyncTransport(
    string peerNode,
    Func<SyncBatch, CancellationToken, Task<SyncAcknowledgement>> receive) : ISyncTransport
{
    /// <inheritdoc />
    public string PeerNode { get; } = peerNode;

    /// <inheritdoc />
    public Task<SyncAcknowledgement> SendAsync(SyncBatch batch, CancellationToken cancellationToken = default)
        => receive(batch, cancellationToken);
}
