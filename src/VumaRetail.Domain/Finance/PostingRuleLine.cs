using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>One posting a <see cref="PostingRule"/> produces when its event type is raised.</summary>
[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class PostingRuleLine : Entity
{
    private PostingRuleLine(Guid tenantId)
        : base(tenantId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private PostingRuleLine()
    {
    }

    /// <summary>The rule this line belongs to.</summary>
    public Guid PostingRuleId { get; private set; }

    /// <summary>The line's position within the rule.</summary>
    public int LineNumber { get; private set; }

    /// <summary>The GL account this line posts to.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>Whether this line debits or credits <see cref="AccountId"/>.</summary>
    public NormalBalance Side { get; private set; }

    /// <summary>Which named amount on the incoming event this line takes its value from.</summary>
    public string AmountKey { get; private set; } = string.Empty;

    /// <summary>Whether this line copies the event's analysis dimensions onto its journal line.</summary>
    public bool InheritDimensions { get; private set; } = true;

    /// <summary>A line-level narration template.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>Builds one rule line. Internal — use <see cref="PostingRule.AddLine"/>.</summary>
    internal static PostingRuleLine Create(
        Guid tenantId,
        Guid postingRuleId,
        int lineNumber,
        Guid accountId,
        NormalBalance side,
        string amountKey,
        bool inheritDimensions,
        string description)
        => new(tenantId)
        {
            PostingRuleId = postingRuleId,
            LineNumber = lineNumber,
            AccountId = accountId,
            Side = side,
            AmountKey = amountKey.Trim(),
            InheritDimensions = inheritDimensions,
            Description = description?.Trim() ?? string.Empty,
        };
}
