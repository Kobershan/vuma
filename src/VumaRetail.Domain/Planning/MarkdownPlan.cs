using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Planning;

/// <summary>
/// A markdown plan: slow stock the planner proposes to mark down, approved through Stage 05 and
/// executed as a real Stage 10 promotion only on activation.
/// </summary>
/// <remarks>
/// <para>
/// A draft or an approved-but-not-activated plan has no pricing effect whatsoever. Only activation
/// creates or updates the promotion, through Stage 10's commands — planning never writes to
/// <c>sales.promotions</c> directly, and Stage 10 remains the pricing authority.
/// </para>
/// <para>
/// A live plan is never edited in place: amendment closes the current version and opens a new one.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class MarkdownPlan : Entity
{
    private readonly List<MarkdownPlanLine> _lines = [];

    private MarkdownPlan()
    {
    }

    private MarkdownPlan(
        Guid tenantId,
        Guid companyId,
        string code,
        string reason,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        int version)
        : base(tenantId)
    {
        AssignCompany(companyId);
        Code = code;
        Reason = reason;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Version = version;
        Status = MarkdownPlanStatus.Draft;
    }

    /// <summary>Human-readable code (<c>MDP-…</c>), unique per tenant.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Why the markdown is proposed, in the planner's words.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>First day the markdown may go live.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Last day, or <c>null</c> for open-ended.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>Version within the plan family. Amendments increment; the old row is kept.</summary>
    public int Version { get; private set; }

    /// <summary>Where the plan stands.</summary>
    public MarkdownPlanStatus Status { get; private set; }

    /// <summary>The Stage 05 approval request, once submitted.</summary>
    public Guid? ApprovalRequestId { get; private set; }

    /// <summary>The Stage 10 promotion, once a step has been activated.</summary>
    public Guid? PromotionId { get; private set; }

    /// <summary>The previous version this plan amends, if any.</summary>
    public Guid? SupersedesPlanId { get; private set; }

    /// <summary>What is being marked down.</summary>
    public IReadOnlyList<MarkdownPlanLine> Lines => _lines;

    /// <summary>Adds a line to a draft plan.</summary>
    /// <exception cref="PlanningRuleException">The plan is not a draft.</exception>
    public MarkdownPlanLine AddLine(
        Guid? itemId,
        Guid? itemVariantId,
        decimal currentPrice,
        decimal proposedDiscountPercent,
        string currency,
        string? abcXyz,
        decimal sellThroughPercent,
        decimal daysOfSupply)
    {
        if (Status is not MarkdownPlanStatus.Draft)
        {
            throw PlanningRuleException.BadTransition("Markdown plan", Status.ToString(), "add line");
        }

        var line = MarkdownPlanLine.Create(
            TenantId, CompanyId!.Value, Id,
            itemId, itemVariantId,
            currentPrice, proposedDiscountPercent, currency,
            abcXyz, sellThroughPercent, daysOfSupply);

        _lines.Add(line);

        return line;
    }

    /// <summary>Submits the plan to Stage 05 approval.</summary>
    /// <param name="approvalRequestId">The pending request Stage 05 raised.</param>
    /// <exception cref="PlanningRuleException">The plan is not a draft, or has no lines.</exception>
    public void Submit(Guid approvalRequestId)
    {
        if (Status is not MarkdownPlanStatus.Draft)
        {
            throw PlanningRuleException.BadTransition("Markdown plan", Status.ToString(), nameof(Submit));
        }

        if (_lines.Count == 0)
        {
            throw PlanningRuleException.BadInput("A markdown plan needs at least one line.");
        }

        ApprovalRequestId = approvalRequestId;
        Status = MarkdownPlanStatus.PendingApproval;
    }

    /// <summary>Records Stage 05's verdict. Approval alone creates no promotion.</summary>
    /// <param name="approved">What Stage 05 decided.</param>
    /// <exception cref="PlanningRuleException">The plan is not awaiting approval.</exception>
    public void ApplyApproval(bool approved)
    {
        if (Status is not MarkdownPlanStatus.PendingApproval)
        {
            throw PlanningRuleException.BadTransition("Markdown plan", Status.ToString(), "apply approval");
        }

        Status = approved ? MarkdownPlanStatus.Approved : MarkdownPlanStatus.Draft;

        if (!approved)
        {
            ApprovalRequestId = null;
        }
    }

    /// <summary>Records that a step was activated as a Stage 10 promotion.</summary>
    /// <param name="promotionId">The promotion Stage 10 created.</param>
    /// <exception cref="PlanningRuleException">The plan is not approved or already active.</exception>
    public void RecordActivation(Guid promotionId)
    {
        if (Status is not (MarkdownPlanStatus.Approved or MarkdownPlanStatus.Active))
        {
            throw PlanningRuleException.BadTransition("Markdown plan", Status.ToString(), "activate");
        }

        PromotionId = promotionId;
        Status = MarkdownPlanStatus.Active;
    }

    /// <summary>Cancels the plan. The caller deactivates any live promotion through Stage 10 first.</summary>
    public void Cancel()
    {
        if (Status is MarkdownPlanStatus.Cancelled or MarkdownPlanStatus.Completed or MarkdownPlanStatus.Amended)
        {
            throw PlanningRuleException.BadTransition("Markdown plan", Status.ToString(), nameof(Cancel));
        }

        Status = MarkdownPlanStatus.Cancelled;
    }

    /// <summary>Closes the plan's course as completed.</summary>
    public void Complete()
    {
        if (Status is not MarkdownPlanStatus.Active)
        {
            throw PlanningRuleException.BadTransition("Markdown plan", Status.ToString(), nameof(Complete));
        }

        Status = MarkdownPlanStatus.Completed;
    }

    /// <summary>Marks this version superseded by an amendment.</summary>
    /// <param name="newVersionId">The new version.</param>
    public void MarkAmended(Guid newVersionId)
    {
        if (Status is MarkdownPlanStatus.Cancelled or MarkdownPlanStatus.Completed or MarkdownPlanStatus.Amended)
        {
            throw PlanningRuleException.BadTransition("Markdown plan", Status.ToString(), "amend");
        }

        ArgumentOutOfRangeException.ThrowIfEqual(Id, newVersionId);
        SupersedesPlanId = newVersionId;
        Status = MarkdownPlanStatus.Amended;
    }

    /// <summary>Opens a draft markdown plan.</summary>
    public static MarkdownPlan Create(
        Guid tenantId,
        Guid companyId,
        string code,
        string reason,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (effectiveTo is { } to && to < effectiveFrom)
        {
            throw PlanningRuleException.BadInput("Markdown effective-to cannot precede effective-from.");
        }

        return new MarkdownPlan(
            tenantId, companyId, code.Trim(), reason.Trim(), effectiveFrom, effectiveTo, 1);
    }

    /// <summary>Opens the next version of a live plan. The old row stays intact.</summary>
    /// <param name="current">The version being superseded.</param>
    /// <param name="code">The new version's code.</param>
    public static MarkdownPlan CreateAmendment(MarkdownPlan current, string code)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var next = new MarkdownPlan(
            current.TenantId, current.CompanyId!.Value,
            code.Trim(), current.Reason,
            current.EffectiveFrom, current.EffectiveTo,
            current.Version + 1);

        current.MarkAmended(next.Id);

        return next;
    }
}
