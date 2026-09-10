using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Crm;

/// <summary>
/// One static segment membership: a customer or lead explicitly placed in a segment
/// (Stage 19). Dynamic segments are evaluated at read time and never persist rows here —
/// writing one is refused before it reaches the database.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class SegmentMember : Entity
{
    private SegmentMember()
    {
    }

    /// <summary>Adds a member to a static segment.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The owning company.</param>
    /// <param name="segment">The segment. Must be static and active.</param>
    /// <param name="memberType">What kind of member.</param>
    /// <param name="memberId">The member's id (partner or lead).</param>
    /// <param name="addedBy">Who added them.</param>
    /// <param name="addedAt">When, UTC. From <c>IClock</c>.</param>
    /// <param name="storeId">The owning store, if any.</param>
    /// <exception cref="DynamicSegmentWriteNotAllowedException">The segment is dynamic.</exception>
    public SegmentMember(
        Guid tenantId,
        Guid companyId,
        Segment segment,
        MemberType memberType,
        Guid memberId,
        string addedBy,
        DateTimeOffset addedAt,
        Guid? storeId = null)
        : base(tenantId, storeId)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentException.ThrowIfNullOrWhiteSpace(addedBy);
        segment.RefuseMemberWrite();
        AssignCompany(companyId);
        SegmentId = segment.Id;
        MemberType = memberType;
        MemberId = memberId;
        AddedBy = addedBy;
        AddedAt = addedAt;
    }

    /// <summary>The segment.</summary>
    public Guid SegmentId { get; private set; }

    /// <summary>What kind of member.</summary>
    public MemberType MemberType { get; private set; }

    /// <summary>The member's id (partner or lead). A plain id, never a cross-schema FK.</summary>
    public Guid MemberId { get; private set; }

    /// <summary>Who added them.</summary>
    public string AddedBy { get; private set; } = string.Empty;

    /// <summary>When, UTC.</summary>
    public DateTimeOffset AddedAt { get; private set; }
}
