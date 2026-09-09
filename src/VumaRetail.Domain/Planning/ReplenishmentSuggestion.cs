using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Planning;

/// <summary>
/// One replenishment suggestion: the engine's proposal for what to buy or move, for one SKU at one
/// location. A suggestion decides nothing — only an explicit accept creates a downstream document.
/// </summary>
/// <remarks>
/// <para>
/// Exactly-once acceptance: <see cref="Accept"/> transitions <c>Open → Accepted</c> and records the
/// downstream document id. A second accept of the same suggestion returns the recorded document
/// rather than creating another one — the handler checks status before dispatching anything.
/// </para>
/// <para>
/// <see cref="SuggestedQuantity"/> is the machine's recommendation and is never mutated: an
/// amend-and-accept records <see cref="AcceptedQuantity"/> beside it.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class ReplenishmentSuggestion : Entity
{
    private ReplenishmentSuggestion()
    {
    }

    private ReplenishmentSuggestion(
        Guid tenantId,
        Guid companyId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal suggestedQuantity,
        string uom,
        SuggestionReason reason,
        SuggestionSource source,
        Guid? sourceCompanyId,
        Guid? sourceLocationId,
        string idempotencyKey,
        bool overOpenToBuy,
        DateTimeOffset raisedAt,
        DateTimeOffset expiresAt)
        : base(tenantId)
    {
        AssignCompany(companyId);
        LocationId = locationId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        SuggestedQuantity = suggestedQuantity;
        Uom = uom;
        Reason = reason;
        Source = source;
        SourceCompanyId = sourceCompanyId;
        SourceLocationId = sourceLocationId;
        IdempotencyKey = idempotencyKey;
        OverOpenToBuy = overOpenToBuy;
        Status = SuggestionStatus.Open;
        RaisedAt = raisedAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>The location that needs stock.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The item, or <c>null</c> for a variant.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant, or <c>null</c> for an item.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>The machine's recommended quantity. Immutable once raised.</summary>
    public decimal SuggestedQuantity { get; private set; }

    /// <summary>The quantity actually accepted, or <c>null</c> until accepted.</summary>
    public decimal? AcceptedQuantity { get; private set; }

    /// <summary>The unit of measure.</summary>
    public string Uom { get; private set; } = string.Empty;

    /// <summary>Why the suggestion exists.</summary>
    public SuggestionReason Reason { get; private set; }

    /// <summary>Where the stock should come from.</summary>
    public SuggestionSource Source { get; private set; }

    /// <summary>For a transfer: the sister company holding the surplus.</summary>
    public Guid? SourceCompanyId { get; private set; }

    /// <summary>For a transfer: the location holding the surplus.</summary>
    public Guid? SourceLocationId { get; private set; }

    /// <summary>Where the suggestion stands.</summary>
    public SuggestionStatus Status { get; private set; }

    /// <summary>The downstream requisition or transfer, once accepted.</summary>
    public Guid? DownstreamDocumentId { get; private set; }

    /// <summary>The run key that raised it: <c>run-date/sku/location</c>. Re-runs upsert on it.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>True when accepting exceeds open-to-buy — a flag, never a refusal.</summary>
    public bool OverOpenToBuy { get; private set; }

    /// <summary>When it was raised, UTC.</summary>
    public DateTimeOffset RaisedAt { get; private set; }

    /// <summary>When an unanswered suggestion lapses, UTC.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Accepts the suggestion as raised, recording the downstream document.</summary>
    /// <param name="downstreamDocumentId">The requisition or transfer the accept created.</param>
    /// <exception cref="PlanningRuleException">The suggestion is not open.</exception>
    public void Accept(Guid downstreamDocumentId)
    {
        if (Status is not SuggestionStatus.Open)
        {
            throw PlanningRuleException.SuggestionNotOpen(Status);
        }

        AcceptedQuantity = SuggestedQuantity;
        DownstreamDocumentId = downstreamDocumentId;
        Status = SuggestionStatus.Accepted;
    }

    /// <summary>Accepts the suggestion with an amended quantity. The recommendation is preserved.</summary>
    /// <param name="amendedQuantity">What goes on the downstream document. Positive.</param>
    /// <param name="downstreamDocumentId">The requisition or transfer the accept created.</param>
    /// <exception cref="PlanningRuleException">The suggestion is not open, or the quantity is not positive.</exception>
    public void AmendAndAccept(decimal amendedQuantity, Guid downstreamDocumentId)
    {
        if (Status is not SuggestionStatus.Open)
        {
            throw PlanningRuleException.SuggestionNotOpen(Status);
        }

        if (amendedQuantity <= 0m)
        {
            throw PlanningRuleException.BadInput("Amended quantity must be positive.");
        }

        AcceptedQuantity = amendedQuantity;
        DownstreamDocumentId = downstreamDocumentId;
        Status = SuggestionStatus.Accepted;
    }

    /// <summary>Rejects the suggestion. Creates nothing downstream.</summary>
    /// <exception cref="PlanningRuleException">The suggestion is not open.</exception>
    public void Reject()
    {
        if (Status is not SuggestionStatus.Open)
        {
            throw PlanningRuleException.SuggestionNotOpen(Status);
        }

        Status = SuggestionStatus.Rejected;
    }

    /// <summary>Lapses an unanswered suggestion past its expiry. Creates nothing downstream.</summary>
    public void Expire()
    {
        if (Status is SuggestionStatus.Open)
        {
            Status = SuggestionStatus.Expired;
        }
    }

    /// <summary>Raises a suggestion.</summary>
    /// <exception cref="PlanningRuleException">Neither or both of item and variant are set, or the shape is inconsistent.</exception>
    public static ReplenishmentSuggestion Raise(
        Guid tenantId,
        Guid companyId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal suggestedQuantity,
        string uom,
        SuggestionReason reason,
        SuggestionSource source,
        Guid? sourceCompanyId,
        Guid? sourceLocationId,
        string idempotencyKey,
        bool overOpenToBuy,
        DateTimeOffset raisedAt,
        DateTimeOffset expiresAt)
    {
        if ((itemId is null) == (itemVariantId is null))
        {
            throw PlanningRuleException.ExactlyOneSku();
        }

        if (suggestedQuantity <= 0m)
        {
            throw PlanningRuleException.BadInput("Suggested quantity must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(uom);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (source is SuggestionSource.Transfer && sourceLocationId is null)
        {
            throw PlanningRuleException.BadInput("A transfer suggestion needs a source location.");
        }

        return new ReplenishmentSuggestion(
            tenantId,
            companyId,
            locationId,
            itemId,
            itemVariantId,
            suggestedQuantity,
            uom.Trim(),
            reason,
            source,
            sourceCompanyId,
            sourceLocationId,
            idempotencyKey.Trim(),
            overOpenToBuy,
            raisedAt,
            expiresAt);
    }
}
