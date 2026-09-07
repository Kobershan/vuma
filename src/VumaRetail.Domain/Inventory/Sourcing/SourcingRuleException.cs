namespace VumaRetail.Domain.Inventory.Sourcing;

/// <summary>
/// Exception for sourcing-related rule violations.
/// </summary>
public sealed class SourcingRuleException : InventoryRuleException
{
    public SourcingRuleException(string code, string message) : base(code, message) { }
}
