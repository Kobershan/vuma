using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Crm;

/// <summary>
/// A named grouping of customers and leads for targeting (Stage 19). Either static (explicit
/// members in <c>crm.segment_members</c>) or dynamic (evaluated at read time from
/// <see cref="QueryExpression"/>), never both.
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class Segment : Entity
{
    private Segment()
    {
    }

    /// <summary>Creates a segment.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The owning company.</param>
    /// <param name="name">Segment name.</param>
    /// <param name="kind">Static or dynamic.</param>
    /// <param name="description">What it is for.</param>
    /// <param name="queryExpression">Filter criteria for dynamic segments.</param>
    /// <param name="storeId">The owning store, if any.</param>
    /// <exception cref="SegmentMixedKindException">Static kind with a query expression.</exception>
    public Segment(
        Guid tenantId,
        Guid companyId,
        string name,
        SegmentKind kind,
        string? description = null,
        string? queryExpression = null,
        Guid? storeId = null)
        : base(tenantId, storeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (kind == SegmentKind.Static && queryExpression is not null)
        {
            throw new SegmentMixedKindException();
        }

        AssignCompany(companyId);
        Name = name.Trim();
        Description = description;
        Kind = kind;
        QueryExpression = queryExpression;
        IsActive = true;
    }

    /// <summary>Segment name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What it is for.</summary>
    public string? Description { get; private set; }

    /// <summary>Static or dynamic.</summary>
    public SegmentKind Kind { get; private set; }

    /// <summary>Filter criteria for dynamic segments. Null for static ones.</summary>
    public string? QueryExpression { get; private set; }

    /// <summary>Whether the segment evaluates. Inactive segments match nobody.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Renames or re-describes the segment. Never changes its kind.</summary>
    /// <param name="name">New name.</param>
    /// <param name="description">New description.</param>
    public void Rename(string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description;
    }

    /// <summary>Deactivates the segment. It matches nobody until reactivated.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Reactivates a deactivated segment.</summary>
    public void Reactivate() => IsActive = true;

    /// <summary>
    /// Guards the static-membership write path: dynamic segments are evaluated at read time and
    /// must never accumulate member rows.
    /// </summary>
    /// <exception cref="DynamicSegmentWriteNotAllowedException">This segment is dynamic.</exception>
    public void RefuseMemberWrite()
    {
        if (Kind == SegmentKind.Dynamic)
        {
            throw new DynamicSegmentWriteNotAllowedException();
        }
    }
}
