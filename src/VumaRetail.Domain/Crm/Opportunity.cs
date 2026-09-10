using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Crm;

/// <summary>
/// A qualified lead with a deal: an expected value, a pipeline stage and a probability
/// (Stage 19). May also be created directly against an existing customer.
/// </summary>
/// <remarks>
/// Value snapshots, not re-derivations (ADR-112 shape): <see cref="ExpectedValue"/> is the
/// money agreed at capture. A reprint a year later shows what was actually proposed.
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class Opportunity : Entity
{
    private Opportunity()
    {
    }

    /// <summary>Opens an opportunity.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The owning company.</param>
    /// <param name="title">Deal title.</param>
    /// <param name="expectedValue">Expected value, in the store's currency.</param>
    /// <param name="probability">Win probability, 0–100.</param>
    /// <param name="leadId">The lead it came from, if any.</param>
    /// <param name="customerId">An existing customer, if created directly against one.</param>
    /// <param name="description">Deal description.</param>
    /// <param name="closeDate">Expected close date, if known.</param>
    /// <param name="storeId">The owning store, if any.</param>
    /// <exception cref="ArgumentOutOfRangeException">Probability outside 0–100.</exception>
    public Opportunity(
        Guid tenantId,
        Guid companyId,
        string title,
        Money expectedValue,
        byte probability,
        Guid? leadId = null,
        Guid? customerId = null,
        string? description = null,
        DateOnly? closeDate = null,
        Guid? storeId = null)
        : base(tenantId, storeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (probability > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(probability), "Probability is 0–100.");
        }

        AssignCompany(companyId);
        Title = title.Trim();
        Description = description;
        ExpectedValue = expectedValue;
        Probability = probability;
        LeadId = leadId;
        CustomerId = customerId;
        CloseDate = closeDate;
        Stage = OpportunityStage.Prospecting;
    }

    /// <summary>Deal title.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Deal description.</summary>
    public string? Description { get; private set; }

    /// <summary>Expected value, in the store's currency. Snapshotted at capture.</summary>
    public Money ExpectedValue { get; private set; }

    /// <summary>Pipeline stage.</summary>
    public OpportunityStage Stage { get; private set; }

    /// <summary>Win probability, 0–100.</summary>
    public byte Probability { get; private set; }

    /// <summary>Expected close date, if known.</summary>
    public DateOnly? CloseDate { get; private set; }

    /// <summary>Why it was lost. Required on <c>Lost</c>.</summary>
    public string? LossReason { get; private set; }

    /// <summary>The lead it came from, if any. A plain id, never a cross-schema FK.</summary>
    public Guid? LeadId { get; private set; }

    /// <summary>The customer it belongs to. Set at win, or at creation for direct ones.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>The user owning the deal, if assigned.</summary>
    public Guid? AssignedTo { get; private set; }

    /// <summary>Moves the deal forward one or more open stages.</summary>
    /// <param name="stage">The new stage. Must not be <c>Won</c>/<c>Lost</c> (use Win/Lose).</param>
    /// <param name="probability">The revised probability, 0–100.</param>
    /// <exception cref="OpportunityStageTransitionException">Illegal transition.</exception>
    public void MoveTo(OpportunityStage stage, byte probability)
    {
        if (Stage is OpportunityStage.Won or OpportunityStage.Lost)
        {
            throw new OpportunityStageTransitionException($"A {Stage} opportunity is closed.");
        }

        if (stage is OpportunityStage.Won or OpportunityStage.Lost)
        {
            throw new OpportunityStageTransitionException("Win or lose the deal explicitly.");
        }

        if (stage < Stage)
        {
            throw new OpportunityStageTransitionException($"A {Stage} deal cannot move back to {stage}.");
        }

        if (probability > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(probability), "Probability is 0–100.");
        }

        Stage = stage;
        Probability = probability;
    }

    /// <summary>Wins the deal against a customer.</summary>
    /// <param name="customerId">The customer it converted into.</param>
    /// <exception cref="OpportunityMissingCustomerException">No customer.</exception>
    public void Win(Guid customerId)
    {
        if (Stage is OpportunityStage.Won or OpportunityStage.Lost)
        {
            throw new OpportunityStageTransitionException($"A {Stage} opportunity is closed.");
        }

        if (customerId == Guid.Empty)
        {
            throw new OpportunityMissingCustomerException();
        }

        CustomerId = customerId;
        Stage = OpportunityStage.Won;
        Probability = 100;
    }

    /// <summary>Loses the deal with a recorded reason.</summary>
    /// <param name="lossReason">Why it was lost. Required for reporting.</param>
    /// <exception cref="OpportunityLossReasonRequiredException">No reason given.</exception>
    public void Lose(string lossReason)
    {
        if (Stage is OpportunityStage.Won or OpportunityStage.Lost)
        {
            throw new OpportunityStageTransitionException($"A {Stage} opportunity is closed.");
        }

        if (string.IsNullOrWhiteSpace(lossReason))
        {
            throw new OpportunityLossReasonRequiredException();
        }

        LossReason = lossReason.Trim();
        Stage = OpportunityStage.Lost;
        Probability = 0;
    }
}
