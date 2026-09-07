namespace VumaRetail.Application.Inventory.Sourcing;

using VumaRetail.Contracts.Inventory.Sourcing;

/// <summary>
/// Command to commit a sourcing plan.
/// Driven by the commit service, not a handler (multi-company rule).
/// </summary>
public sealed class CommitSourcingPlanCommand : ICommand<SourcingCommitResultDto>
{
    public CommitSourcingPlanCommand(
        Guid orderLineId,
        SourcingPlanDto plan,
        Guid orderingCompanyId,
        string idempotencyKey)
    {
        OrderLineId = orderLineId;
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        OrderingCompanyId = orderingCompanyId;
        IdempotencyKey = idempotencyKey ?? throw new ArgumentNullException(nameof(idempotencyKey));
    }

    public Guid OrderLineId { get; }
    public SourcingPlanDto Plan { get; }
    public Guid OrderingCompanyId { get; }
    public string IdempotencyKey { get; }
}
