using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>
/// Tenant-configured data mapping one financial event type to a balanced set of GL postings
/// (CLAUDE.md §7 rule 12, ADR-016).
/// </summary>
/// <remarks>
/// <para>
/// This is the mechanism rule 12 refers to. A producer module — POS, procurement, and eventually
/// every module that moves money or value — raises an <c>IFinancialEvent</c> naming an event type
/// and a set of amounts; it never names an account. This rule, not the producer, decides which
/// accounts the event affects and on which side. A new event type is a new row here, not a code
/// change in the producing module and not a code change in Finance either.
/// </para>
/// <para>
/// Only one active rule per <c>(TenantId, EventType)</c> is meaningful; the posting engine takes the
/// most recently created active one. Deactivating a rule rather than deleting it keeps history
/// readable — a journal already posted still shows which rule produced it.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class PostingRule : Entity
{
    private readonly List<PostingRuleLine> _lines = [];

    private PostingRule(Guid tenantId)
        : base(tenantId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private PostingRule()
    {
    }

    /// <summary>
    /// The financial event type this rule answers, for example <c>ar.invoice.posted</c> or
    /// <c>pos.sale.tendered</c>.
    /// </summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>A description for the person configuring it.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>Whether the posting engine will use this rule.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>The postings this rule produces, in order.</summary>
    public IReadOnlyList<PostingRuleLine> Lines => _lines;

    /// <summary>Defines a new rule with no lines yet. Add lines with <see cref="AddLine"/>.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="eventType">The financial event type this answers.</param>
    /// <param name="description">A description for the person configuring it.</param>
    public static PostingRule Define(Guid tenantId, string eventType, string description = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        return new PostingRule(tenantId)
        {
            EventType = eventType.Trim(),
            Description = description?.Trim() ?? string.Empty,
            IsActive = true,
        };
    }

    /// <summary>
    /// Adds one posting line to the rule.
    /// </summary>
    /// <param name="accountId">The GL account this line posts to.</param>
    /// <param name="side">Whether this line debits or credits the account.</param>
    /// <param name="amountKey">
    /// Which named amount on the incoming event this line takes its value from, for example
    /// <c>Net</c>, <c>Tax</c> or <c>Gross</c>.
    /// </param>
    /// <param name="inheritDimensions">
    /// Whether this line copies the event's analysis dimensions onto the journal line it produces.
    /// </param>
    /// <param name="description">A line-level narration template.</param>
    public PostingRule AddLine(
        Guid accountId,
        NormalBalance side,
        string amountKey,
        bool inheritDimensions = true,
        string description = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(amountKey);

        _lines.Add(PostingRuleLine.Create(
            TenantId, Id, _lines.Count + 1, accountId, side, amountKey, inheritDimensions, description));

        return this;
    }

    /// <summary>Deactivates the rule. The posting engine will no longer use it.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Reactivates a deactivated rule.</summary>
    public void Activate() => IsActive = true;
}
