using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Sync;
using VumaRetail.Sync.Protocol;

namespace VumaRetail.Sync.Commands;

/// <summary>
/// Applies a batch of replicated operations from a peer node.
/// </summary>
/// <remarks>
/// <para>
/// Classified <see cref="ReadOnlyExemption.OfflineFlush"/>, and that is the correct reading of
/// ADR-028 rather than a convenient one. The exemption covers "flushing data a terminal already
/// captured before enforcement began" — and this command is exactly that path, generalised to any
/// tier. The sales in an inbound batch <em>have already happened</em>; refusing to record them would
/// destroy a tenant's books to make a billing point, which is the specific outcome ADR-028's third
/// non-negotiable rule forbids.
/// </para>
/// <para>
/// It does not widen the carve-out set: <c>OfflineFlush</c> already existed and this is its first
/// claimant. Nothing about it lets a read-only tenant <em>originate</em> a write — a till that is
/// read-only refuses the sale at slot 50 and never captures anything for this command to flush.
/// </para>
/// </remarks>
/// <param name="Batch">The batch, already deserialised from the wire.</param>
[CommandSideEffect(SideEffect.Write, Exemption = ReadOnlyExemption.OfflineFlush)]
public sealed record ReceiveSyncBatchCommand(SyncBatch Batch) : ICommand<SyncAcknowledgement>;

/// <summary>Rejects a malformed batch before the receiver touches a row.</summary>
public sealed class ReceiveSyncBatchCommandValidator : AbstractValidator<ReceiveSyncBatchCommand>
{
    /// <summary>The most operations one batch may carry.</summary>
    /// <remarks>
    /// A ceiling rather than a tuning knob. The whole batch runs in one transaction, and a peer that
    /// sent a hundred thousand operations at once would hold a write transaction open on a trading
    /// store for as long as it took — which is a denial of service that looks like a slow network.
    /// The dispatcher chunks; this is what stops a peer that does not.
    /// </remarks>
    public const int MaxOperationsPerBatch = 500;

    /// <summary>Builds the rules.</summary>
    public ReceiveSyncBatchCommandValidator()
    {
        RuleFor(command => command.Batch).NotNull();
        RuleFor(command => command.Batch.SourceNode)
            .NotEmpty()
            .MaximumLength(Domain.Primitives.HlcStamp.NodeIdMaxLength);
        RuleFor(command => command.Batch.TenantId).NotEmpty();
        RuleFor(command => command.Batch.Operations).NotNull();
        RuleFor(command => command.Batch.Operations.Count)
            .LessThanOrEqualTo(MaxOperationsPerBatch)
            .WithMessage($"A batch carries at most {MaxOperationsPerBatch} operations.");
        RuleForEach(command => command.Batch.Operations).ChildRules(operation =>
        {
            operation.RuleFor(item => item.OperationId).NotEmpty();
            operation.RuleFor(item => item.EntityType).NotEmpty().MaximumLength(128);
            operation.RuleFor(item => item.EntityId).NotEmpty();
            operation.RuleFor(item => item.Payload).NotEmpty();
        });
    }
}

/// <summary>Hands the batch to the receiver, inside the pipeline's transaction.</summary>
/// <param name="receiver">The receiver.</param>
public sealed class ReceiveSyncBatchCommandHandler(SyncReceiver receiver)
    : ICommandHandler<ReceiveSyncBatchCommand, SyncAcknowledgement>
{
    /// <inheritdoc />
    public async Task<SyncAcknowledgement> HandleAsync(
        ReceiveSyncBatchCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await receiver.ReceiveAsync(command.Batch, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Settles a conflict by choosing which version of the record stands.</summary>
/// <param name="ConflictId">The conflict entry.</param>
/// <param name="Resolution">Which version stands.</param>
/// <param name="Note">Why. Required — a person is overruling the system about a business record.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ResolveConflictCommand(Guid ConflictId, ConflictResolution Resolution, string Note)
    : ICommand<Unit>;

/// <summary>Rejects a resolve-conflict command before it reaches the handler.</summary>
public sealed class ResolveConflictCommandValidator : AbstractValidator<ResolveConflictCommand>
{
    /// <summary>Builds the rules.</summary>
    public ResolveConflictCommandValidator()
    {
        RuleFor(command => command.ConflictId).NotEmpty();
        RuleFor(command => command.Resolution).NotEqual(ConflictResolution.Unresolved);
        RuleFor(command => command.Note).NotEmpty().MaximumLength(1024);
    }
}

/// <summary>
/// Settles a conflict, applying the remote version where that is what was chosen.
/// </summary>
/// <remarks>
/// Choosing <see cref="ConflictResolution.TookRemote"/> writes the remote version over the local row
/// <em>now</em>, from the copy the conflict entry kept. That is why the entry keeps both versions in
/// full rather than a diff: by the time somebody reviews it, the batch it came from is long gone and
/// the sending node has moved on.
/// </remarks>
/// <param name="conflicts">The review queue.</param>
/// <param name="registry">Resolves the entity type the conflict is about.</param>
/// <param name="replicas">Applies the remote version, where that is the decision.</param>
/// <param name="principal">Who is settling it (R6).</param>
/// <param name="clock">When.</param>
public sealed class ResolveConflictCommandHandler(
    IConflictRepository conflicts,
    IReplicationRegistry registry,
    IReplicaWriter replicas,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<ResolveConflictCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(ResolveConflictCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ConflictEntry entry = await conflicts.FindAsync(command.ConflictId, cancellationToken).ConfigureAwait(false)
            ?? throw new ConflictNotFoundException(command.ConflictId);

        if (command.Resolution == ConflictResolution.TookRemote)
        {
            Type entityType = registry.ResolveEntityType(entry.EntityType)
                ?? throw new UnknownReplicatedEntityException(entry.EntityType);

            // Outbox capture is deliberately *not* suppressed here, unlike in the receiver. A person
            // choosing the remote version is a decision this node made, and the other tiers have to
            // hear about it — otherwise the losing node keeps its own version and raises the same
            // conflict again on the next change to the row.
            await replicas.ApplyAsync(
                entityType,
                new SyncOperation(
                    Guid.NewGuid(),
                    entry.EntityType,
                    entry.EntityId,
                    SyncOperationKind.Upsert,
                    Domain.Primitives.HlcStamp.Parse(entry.RemoteStamp),
                    entry.RemoteVersion,
                    clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }

        entry.Resolve(command.Resolution, principal.Principal, command.Note, clock.UtcNow);

        return Unit.Value;
    }
}

/// <summary>Thrown when a conflict names an entity type this node no longer knows.</summary>
/// <param name="entityTypeName">The entity type name recorded on the conflict.</param>
public sealed class UnknownReplicatedEntityException(string entityTypeName)
    : Domain.Primitives.DomainException(
        "SYNC_ENTITY_UNKNOWN",
        $"This node does not replicate '{entityTypeName}'. It may have been removed since the "
        + "conflict was raised.",
        Domain.Primitives.DomainProblemKind.NotFound);
