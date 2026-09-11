#pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Connect;

public enum ConnectClaimReason { ShortDelivery = 1, Damaged = 2, WrongItem = 3 }
public enum ConnectClaimStatus { Open = 1, Credited = 2, Rejected = 3 }

[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class ConnectDeliveryClaim : Entity
{
    private ConnectDeliveryClaim(Guid retailerTenantId, Guid supplierTenantId, Guid connectionId, Guid orderId,
        Guid orderLineId, string claimNumber, ConnectClaimReason reason, Quantity quantity, Money amount,
        string description, DateTimeOffset raisedAt) : base(retailerTenantId)
    {
        RetailerTenantId = retailerTenantId; SupplierTenantId = supplierTenantId; ConnectionId = connectionId;
        OrderId = orderId; OrderLineId = orderLineId; ClaimNumber = claimNumber.Trim(); Reason = reason;
        Quantity = quantity; Amount = amount; Description = description.Trim(); RaisedAt = raisedAt;
        Status = ConnectClaimStatus.Open;
    }
    private ConnectDeliveryClaim() { }
    public Guid RetailerTenantId { get; private set; }
    public Guid SupplierTenantId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid OrderLineId { get; private set; }
    public string ClaimNumber { get; private set; } = string.Empty;
    public ConnectClaimReason Reason { get; private set; }
    public Quantity Quantity { get; private set; }
    public Money Amount { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public ConnectClaimStatus Status { get; private set; }
    public string? CreditNoteReference { get; private set; }
    public DateTimeOffset RaisedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public static ConnectDeliveryClaim Raise(Guid retailerTenantId, Guid supplierTenantId, Guid connectionId,
        Guid orderId, Guid orderLineId, string claimNumber, ConnectClaimReason reason, Quantity quantity,
        Money amount, string description, DateTimeOffset raisedAt)
    {
        if (retailerTenantId == Guid.Empty || supplierTenantId == Guid.Empty || retailerTenantId == supplierTenantId)
            throw new ArgumentException("A claim requires two different tenants.");
        if (orderId == Guid.Empty || orderLineId == Guid.Empty || string.IsNullOrWhiteSpace(claimNumber) ||
            quantity.Value <= 0 || amount.Amount < 0 || string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("A claim requires a valid order line, quantity, amount and description.");
        return new(retailerTenantId, supplierTenantId, connectionId, orderId, orderLineId, claimNumber, reason,
            quantity, amount, description, raisedAt);
    }

    public void IssueCreditNote(string reference, DateTimeOffset resolvedAt)
    {
        if (Status != ConnectClaimStatus.Open) throw new InvalidOperationException("Only an open claim can be credited.");
        if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("A credit note reference is required.");
        CreditNoteReference = reference.Trim(); Status = ConnectClaimStatus.Credited; ResolvedAt = resolvedAt;
    }

    public void Reject(DateTimeOffset resolvedAt)
    {
        if (Status != ConnectClaimStatus.Open) throw new InvalidOperationException("Only an open claim can be rejected.");
        Status = ConnectClaimStatus.Rejected; ResolvedAt = resolvedAt;
    }
}
