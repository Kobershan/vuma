using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Workflow;

/// <summary>
/// One decision made on an <see cref="ApprovalRequest"/>. Append-only, independent of the request's
/// own mutable status.
/// </summary>
/// <remarks>
/// The request row tells you where things stand right now; this table tells you what actually
/// happened and in what order, which is the question an investigation into "who approved this and
/// when" asks. Kept separate rather than folded into the request so the full history survives however
/// many times a request is decided against (in practice, exactly once, since a decision ends it) —
/// and so two nodes recording decisions on the same request converge by accumulation rather than by
/// one overwriting the other (ADR-007).
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ApprovalDecisionEntry : Entity
{
    private ApprovalDecisionEntry(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ApprovalDecisionEntry()
    {
    }

    /// <summary>The request this decision belongs to.</summary>
    public Guid ApprovalRequestId { get; private set; }

    /// <summary>The deciding principal.</summary>
    public string DecidedBy { get; private set; } = string.Empty;

    /// <summary>What was decided.</summary>
    public ApprovalDecisionOutcome Outcome { get; private set; }

    /// <summary>Free text from the decider.</summary>
    public string? Comment { get; private set; }

    /// <summary>When, UTC.</summary>
    public DateTimeOffset DecidedAt { get; private set; }

    /// <summary>The longest comment that will be stored.</summary>
    public const int MaxCommentLength = 1024;

    /// <summary>Records a decision.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="storeId">The store, if the request has one.</param>
    /// <param name="approvalRequestId">The request being decided.</param>
    /// <param name="decidedBy">The deciding principal.</param>
    /// <param name="outcome">What was decided.</param>
    /// <param name="comment">Free text from the decider.</param>
    /// <param name="now">The instant, UTC.</param>
    /// <returns>The entry.</returns>
    public static ApprovalDecisionEntry Record(
        Guid tenantId,
        Guid? storeId,
        Guid approvalRequestId,
        string decidedBy,
        ApprovalDecisionOutcome outcome,
        string? comment,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);

        return new ApprovalDecisionEntry(tenantId, storeId)
        {
            ApprovalRequestId = approvalRequestId,
            DecidedBy = decidedBy,
            Outcome = outcome,
            Comment = comment,
            DecidedAt = now,
        };
    }
}
