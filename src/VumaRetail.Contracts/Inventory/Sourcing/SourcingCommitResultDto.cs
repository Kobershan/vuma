namespace VumaRetail.Contracts.Inventory.Sourcing;

public sealed class SourcingCommitResultDto
{
    public Guid IntentId { get; set; }
    public List<Guid> SplitOrderIds { get; set; } = new();
    public Dictionary<Guid, decimal> ReservationsByCompany { get; set; } = new();
}
