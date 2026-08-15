using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Contracts.Backup;
using VumaRetail.Contracts.Sync;
using VumaRetail.Domain.Backup;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;
using VumaRetail.Sync.Commands;
using VumaRetail.Sync.Permissions;
using VumaRetail.Sync.Queries;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Sync;

/// <summary>
/// The replication and backup endpoints, served by the store server and the cloud alike.
/// </summary>
/// <remarks>
/// <para>
/// One set of routes for both tiers, because it is one protocol: a terminal posts a batch to its
/// store server exactly as a store server posts one to the cloud (ADR-003's three-tier chain,
/// handled generically). The receiver decides what to do from the entity's own declaration and the
/// two nodes' tiers, so the same code is correct at both ends.
/// </para>
/// <para>
/// <c>POST /sync/batches</c> is the highest-privilege endpoint in the system: it writes replicated
/// rows without passing a single business rule. It is gated on <c>sync.batch.push</c>, which belongs
/// to a peer node's service credential and to nobody who signs in with a password — and the receiver
/// refuses a batch whose tenant is not the caller's, whatever the permission says.
/// </para>
/// </remarks>
public static class SyncEndpoints
{
    /// <summary>Maps the sync and backup endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaSync(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();
        RouteGroupBuilder sync = api.MapGroup("/sync").WithTags("Sync");

        sync.MapPost("/batches", ReceiveBatchAsync)
            .RequirePermission(SyncPermissions.BatchPush)
            .Produces<SyncBatchResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Accepts a batch of replicated operations from a peer node.")
            .WithDescription(
                "Idempotent on (source node, operation id): replaying a batch answers Duplicate "
                + "rather than failing, which is how an at-least-once transport is meant to behave.");

        sync.MapGet("/status", (IDispatcher dispatcher, CancellationToken cancellationToken)
                => dispatcher.QueryAsync(new GetSyncStatusQuery(), cancellationToken))
            .RequirePermission(SyncPermissions.StatusView)
            .Produces<SyncStatusResponse>()
            .WithSummary("This node's replication health: cursors, queue depth, open conflicts.");

        sync.MapGet("/conflicts", (
                IDispatcher dispatcher,
                bool? openOnly,
                int? limit,
                CancellationToken cancellationToken)
                => dispatcher.QueryAsync(
                    new GetConflictQueueQuery(openOnly ?? true, limit ?? 50),
                    cancellationToken))
            .RequirePermission(SyncPermissions.ConflictView)
            .Produces<IReadOnlyList<ConflictResponse>>()
            .WithSummary("The conflict review queue, with both versions of each divergence (ADR-007).");

        sync.MapPost("/conflicts/{conflictId:guid}/resolve", ResolveConflictAsync)
            .RequirePermission(SyncPermissions.ConflictResolve)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Chooses which version of a diverged record stands.");

        RouteGroupBuilder backup = api.MapGroup("/backup").WithTags("Backup");

        backup.MapGet("/snapshots", (IDispatcher dispatcher, int? limit, CancellationToken cancellationToken)
                => dispatcher.QueryAsync(new ListBackupSnapshotsQuery(limit ?? 50), cancellationToken))
            .RequirePermission(BackupPermissions.SnapshotView)
            .Produces<IReadOnlyList<BackupSnapshotResponse>>()
            .WithSummary("The snapshot ledger, including when each was last verified and restored (R4).");

        backup.MapPost("/snapshots", CreateSnapshotAsync)
            .RequirePermission(BackupPermissions.SnapshotCreate)
            .Produces<BackupSnapshotResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Takes a snapshot now: dump, encrypt, upload, checksum.");

        backup.MapPost("/snapshots/{snapshotId:guid}/verify", VerifySnapshotAsync)
            .RequirePermission(BackupPermissions.SnapshotVerify)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Reads a snapshot back from the vault and confirms its checksum.");

        return endpoints;
    }

    private static async Task<Ok<SyncBatchResponse>> ReceiveBatchAsync(
        SyncBatchRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        SyncAcknowledgement acknowledgement = await dispatcher.SendAsync(
            new ReceiveSyncBatchCommand(ToBatch(request)),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new SyncBatchResponse(
            acknowledgement.ReceiverNode,
            acknowledgement.AcknowledgedStamp.ToString(),
            [.. acknowledgement.Results.Select(result => new SyncOperationOutcomeDto(
                result.OperationId,
                result.Outcome.ToString(),
                result.ConflictId,
                result.Detail))]));
    }

    private static async Task<NoContent> ResolveConflictAsync(
        Guid conflictId,
        ResolveConflictRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await dispatcher.SendAsync(
            new ResolveConflictCommand(conflictId, ParseResolution(request.Resolution), request.Note),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<Accepted<BackupSnapshotResponse>> CreateSnapshotAsync(
        CreateBackupSnapshotRequest? request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        BackupKind kind = ParseBackupKind(request?.Kind ?? nameof(BackupKind.Full));

        Guid snapshotId = await dispatcher
            .SendAsync(new CreateBackupSnapshotCommand(kind), cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<BackupSnapshotResponse> ledger = await dispatcher
            .QueryAsync(new ListBackupSnapshotsQuery(ListBackupSnapshotsQueryHandler.MaxLimit), cancellationToken)
            .ConfigureAwait(false);

        BackupSnapshotResponse snapshot = ledger.First(entry => entry.Id == snapshotId);

        // 202, not 201. The snapshot is complete by the time this returns, but "accepted" is the
        // honest status for something the caller should verify rather than assume — and it leaves
        // room for the same route to hand off to a queue when a database gets big enough to need it.
        return TypedResults.Accepted($"/api/v1/backup/snapshots/{snapshotId}", snapshot);
    }

    private static async Task<NoContent> VerifySnapshotAsync(
        Guid snapshotId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new VerifyBackupSnapshotCommand(snapshotId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static SyncBatch ToBatch(SyncBatchRequest request)
        => new(
            request.SourceNode,
            ParseNodeKind(request.SourceKind),
            request.TenantId,
            request.StoreId,
            [.. (request.Operations ?? []).Select(operation => new SyncOperation(
                operation.OperationId,
                operation.EntityType,
                operation.EntityId,
                ParseOperationKind(operation.Operation),
                HlcStamp.Parse(operation.Stamp),
                operation.Payload,
                operation.OccurredAt))]);

    /// <summary>
    /// Parses an enum from the wire, refusing anything unrecognised.
    /// </summary>
    /// <remarks>
    /// Never <c>Enum.TryParse</c> with a fallback. A <c>SourceKind</c> that quietly defaulted to
    /// <c>Store</c> would let a terminal win a <c>StoreWins</c> conflict against the store server, and
    /// an <c>Operation</c> that defaulted to <c>Upsert</c> would turn a typo into a resurrected row.
    /// The 400 is the right answer, and <c>DomainProblemKind.Malformed</c> is what produces it.
    /// </remarks>
    private static NodeKind ParseNodeKind(string value)
        => Enum.TryParse(value, ignoreCase: true, out NodeKind kind) && Enum.IsDefined(kind)
            ? kind
            : throw new MalformedSyncFieldException(nameof(SyncBatchRequest.SourceKind), value);

    private static SyncOperationKind ParseOperationKind(string value)
        => Enum.TryParse(value, ignoreCase: true, out SyncOperationKind kind) && Enum.IsDefined(kind)
            ? kind
            : throw new MalformedSyncFieldException(nameof(SyncOperationDto.Operation), value);

    private static ConflictResolution ParseResolution(string value)
        => Enum.TryParse(value, ignoreCase: true, out ConflictResolution resolution) && Enum.IsDefined(resolution)
            ? resolution
            : throw new MalformedSyncFieldException(nameof(ResolveConflictRequest.Resolution), value);

    private static BackupKind ParseBackupKind(string value)
        => Enum.TryParse(value, ignoreCase: true, out BackupKind kind) && Enum.IsDefined(kind)
            ? kind
            : throw new MalformedSyncFieldException(nameof(CreateBackupSnapshotRequest.Kind), value);
}

/// <summary>Thrown when a field on a sync or backup request is not one of the values it may be.</summary>
/// <param name="field">The field.</param>
/// <param name="value">What was sent.</param>
public sealed class MalformedSyncFieldException(string field, string value)
    : DomainException(
        "SYNC_FIELD_MALFORMED",
        $"'{value}' is not a valid {field}.",
        DomainProblemKind.Malformed);
