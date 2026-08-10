using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sync;

/// <summary>
/// A divergence the entity's <see cref="ConflictPolicy"/> would not settle, waiting for a person.
/// </summary>
/// <remarks>
/// <para>
/// ADR-007 routes ambiguous business conflicts — two stores editing one supplier is the canonical
/// case — to a review queue "instead of silently losing data". This is that queue, and the load
/// bearing part is that <b>both versions are kept</b>. An escalation that discarded the loser would
/// be a last-writer-wins with extra ceremony and a longer delay.
/// </para>
/// <para>
/// While an entry is unresolved the local row is unchanged. That is the safe default: the node that
/// detected the conflict is the node whose users are looking at the row, and changing it under them
/// to a version nobody has approved is worse than leaving it alone.
/// </para>
/// <para>
/// Replicated <see cref="ReplicationScope.StoreToCloud"/> so the queue is visible in the tenant
/// roll-up and, from Stage 30, in the Android admin app. A review queue only one machine in the
/// building can see is a review queue nobody reviews. Its own conflict policy is
/// <see cref="ConflictPolicy.LastWriterWins"/> and not <see cref="ConflictPolicy.ManualReview"/>:
/// a conflict about a conflict entry has no business meaning and would recurse.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.LastWriterWins)]
public sealed class ConflictEntry : Entity
{
    private ConflictEntry(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ConflictEntry()
    {
    }

    /// <summary>The CLR type name of the entity that diverged.</summary>
    public string EntityType { get; private set; } = string.Empty;

    /// <summary>The primary key of the row that diverged.</summary>
    public Guid EntityId { get; private set; }

    /// <summary>The operation that could not be applied.</summary>
    public Guid OperationId { get; private set; }

    /// <summary>The node the losing version came from.</summary>
    public string SourceNode { get; private set; } = string.Empty;

    /// <summary>This node's version of the row at the moment of detection, as JSON.</summary>
    public string LocalVersion { get; private set; } = "{}";

    /// <summary>The peer's version, as JSON. Kept in full — this is the point of the queue.</summary>
    public string RemoteVersion { get; private set; } = "{}";

    /// <summary>This node's ordering stamp for the row, in its sortable string form.</summary>
    public string LocalStamp { get; private set; } = HlcStamp.MinValue.ToString();

    /// <summary>The peer's ordering stamp, in its sortable string form.</summary>
    public string RemoteStamp { get; private set; } = HlcStamp.MinValue.ToString();

    /// <summary>When the divergence was detected, UTC.</summary>
    public DateTimeOffset DetectedAt { get; private set; }

    /// <summary>How it was settled, or <see cref="ConflictResolution.Unresolved"/> while it waits.</summary>
    public ConflictResolution Resolution { get; private set; } = ConflictResolution.Unresolved;

    /// <summary>When a person settled it, UTC, or <c>null</c>.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>Who settled it. R6's "who", for a decision that overrode the system's.</summary>
    public string? ResolvedBy { get; private set; }

    /// <summary>Why they settled it that way. Free text, and required — see <see cref="Resolve"/>.</summary>
    public string? ResolutionNote { get; private set; }

    /// <summary>True while the entry is waiting for a person.</summary>
    public bool IsOpen => Resolution == ConflictResolution.Unresolved;

    /// <summary>Opens a conflict for review, keeping both versions.</summary>
    /// <param name="tenantId">The tenant the row belongs to.</param>
    /// <param name="storeId">The store the row belongs to, if any.</param>
    /// <param name="entityType">The CLR type name of the diverged entity.</param>
    /// <param name="entityId">The primary key of the diverged row.</param>
    /// <param name="operationId">The operation that could not be applied.</param>
    /// <param name="sourceNode">The node the remote version came from.</param>
    /// <param name="localVersion">This node's version, as JSON.</param>
    /// <param name="remoteVersion">The peer's version, as JSON.</param>
    /// <param name="localStamp">This node's stamp for the row.</param>
    /// <param name="remoteStamp">The peer's stamp for the row.</param>
    /// <param name="detectedAt">When the divergence was detected, UTC.</param>
    public static ConflictEntry Open(
        Guid tenantId,
        Guid? storeId,
        string entityType,
        Guid entityId,
        Guid operationId,
        string sourceNode,
        string localVersion,
        string remoteVersion,
        HlcStamp localStamp,
        HlcStamp remoteStamp,
        DateTimeOffset detectedAt)
        => new(tenantId, storeId)
        {
            EntityType = entityType,
            EntityId = entityId,
            OperationId = operationId,
            SourceNode = sourceNode,
            LocalVersion = localVersion,
            RemoteVersion = remoteVersion,
            LocalStamp = localStamp.ToString(),
            RemoteStamp = remoteStamp.ToString(),
            DetectedAt = detectedAt,
        };

    /// <summary>Settles the conflict.</summary>
    /// <param name="resolution">Which version stands.</param>
    /// <param name="resolvedBy">The principal settling it.</param>
    /// <param name="note">Why. Required — see the remarks.</param>
    /// <param name="at">When, UTC.</param>
    /// <exception cref="ConflictAlreadyResolvedException">The entry has already been settled.</exception>
    /// <remarks>
    /// The note is mandatory because this is the one place in Vuma where a person overrules the
    /// system about which version of a business record is real. Six months later somebody will ask
    /// why a supplier's payment terms are what they are, and "a human chose the store's version"
    /// answers nothing on its own.
    /// </remarks>
    public void Resolve(ConflictResolution resolution, string resolvedBy, string note, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(note);

        if (resolution == ConflictResolution.Unresolved)
        {
            throw new ConflictNotSettledException(Id);
        }

        if (!IsOpen)
        {
            throw new ConflictAlreadyResolvedException(Id);
        }

        Resolution = resolution;
        ResolvedBy = resolvedBy;
        ResolutionNote = note;
        ResolvedAt = at;
    }
}

/// <summary>Thrown when a conflict that has already been settled is settled again.</summary>
/// <param name="conflictId">The conflict entry.</param>
public sealed class ConflictAlreadyResolvedException(Guid conflictId)
    : DomainException(
        "SYNC_CONFLICT_ALREADY_RESOLVED",
        $"Conflict {conflictId} has already been resolved. Re-resolving it would overwrite the "
        + "decision and the reason recorded for it.",
        DomainProblemKind.Conflict);

/// <summary>Thrown when a caller asks to resolve a conflict to "unresolved".</summary>
/// <param name="conflictId">The conflict entry.</param>
public sealed class ConflictNotSettledException(Guid conflictId)
    : DomainException(
        "SYNC_CONFLICT_RESOLUTION_REQUIRED",
        $"Resolving conflict {conflictId} means choosing the local or the remote version. "
        + "'Unresolved' is the state it is already in.",
        DomainProblemKind.Malformed);

/// <summary>Thrown when a conflict is asked for and this tenant has no such entry.</summary>
/// <param name="conflictId">The conflict entry that was asked for.</param>
public sealed class ConflictNotFoundException(Guid conflictId)
    : DomainException(
        "SYNC_CONFLICT_NOT_FOUND",
        $"No conflict {conflictId}.",
        DomainProblemKind.NotFound);
