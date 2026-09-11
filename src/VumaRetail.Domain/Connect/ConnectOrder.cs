#pragma warning disable CS1591, IDE0011, CA1062
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Connect;

public enum ConnectOrderStatus
{
    Submitted = 1,
    Confirmed = 2,
    PartiallyConfirmed = 3,
    Rejected = 4,
    Dispatched = 5,
    Received = 6,
    Cancelled = 7
}

[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class ConnectOrder : Entity
{
    private readonly List<ConnectOrderLine> _lines = [];

    private ConnectOrder(Guid retailerTenantId, Guid supplierTenantId, Guid connectionId,
        Guid purchaseOrderId, string orderNumber, DateTimeOffset submittedAt) : base(retailerTenantId)
    {
        RetailerTenantId = retailerTenantId;
        SupplierTenantId = supplierTenantId;
        ConnectionId = connectionId;
        PurchaseOrderId = purchaseOrderId;
        OrderNumber = orderNumber.Trim();
        SubmittedAt = submittedAt;
        Status = ConnectOrderStatus.Submitted;
    }

    private ConnectOrder() { }

    public Guid RetailerTenantId { get; private set; }
    public Guid SupplierTenantId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public string OrderNumber { get; private set; } = string.Empty;
    public ConnectOrderStatus Status { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset? PromisedAt { get; private set; }
    public DateTimeOffset? DispatchedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? DispatchNoteNumber { get; private set; }
    public IReadOnlyList<ConnectOrderLine> Lines => _lines;

    public static ConnectOrder Place(Guid retailerTenantId, Guid supplierTenantId, Guid connectionId,
        Guid purchaseOrderId, string orderNumber, DateTimeOffset submittedAt)
    {
        if (retailerTenantId == Guid.Empty || supplierTenantId == Guid.Empty || retailerTenantId == supplierTenantId)
            throw new ArgumentException("An order requires two different tenants.");
        if (connectionId == Guid.Empty || purchaseOrderId == Guid.Empty || string.IsNullOrWhiteSpace(orderNumber))
            throw new ArgumentException("An order requires a connection, purchase order and number.");
        return new(retailerTenantId, supplierTenantId, connectionId, purchaseOrderId, orderNumber, submittedAt);
    }

    public ConnectOrderLine AddLine(string supplierSku, string description, Quantity quantity, Money unitPrice)
    {
        if (Status != ConnectOrderStatus.Submitted) throw new InvalidOperationException("Only a submitted order can be edited.");
        if (_lines.Any(line => line.SupplierSku.Equals(supplierSku.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("The supplier SKU is already on this order.");
        ConnectOrderLine line = ConnectOrderLine.Create(TenantId, Id, supplierSku, description, quantity, unitPrice);
        _lines.Add(line);
        return line;
    }

    public void Confirm(IReadOnlyDictionary<Guid, Quantity> quantities, DateTimeOffset promisedAt)
    {
        EnsureHasLines();
        EnsureOpenForSupplier();
        if (quantities.Count == 0) throw new ArgumentException("At least one line must be confirmed.");
        foreach (ConnectOrderLine line in _lines)
        {
            if (!quantities.TryGetValue(line.Id, out Quantity quantity)) continue;
            line.Confirm(quantity);
        }
        Status = _lines.All(line => line.ConfirmedQuantity == line.RequestedQuantity)
            ? ConnectOrderStatus.Confirmed
            : ConnectOrderStatus.PartiallyConfirmed;
        PromisedAt = promisedAt;
    }

    public void Reject(string reason)
    {
        EnsureHasLines();
        EnsureOpenForSupplier();
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A rejection reason is required.");
        RejectionReason = reason.Trim();
        Status = ConnectOrderStatus.Rejected;
    }

    public void Dispatch(string dispatchNoteNumber, IReadOnlyDictionary<Guid, Quantity> quantities, DateTimeOffset dispatchedAt)
    {
        EnsureHasLines();
        if (Status is not (ConnectOrderStatus.Confirmed or ConnectOrderStatus.PartiallyConfirmed))
            throw new InvalidOperationException("Only a confirmed order can be dispatched.");
        if (string.IsNullOrWhiteSpace(dispatchNoteNumber)) throw new ArgumentException("A dispatch note number is required.");
        foreach (ConnectOrderLine line in _lines)
        {
            if (!quantities.TryGetValue(line.Id, out Quantity quantity)) continue;
            line.Dispatch(quantity);
        }
        if (_lines.Any(line => line.DispatchedQuantity > line.ConfirmedQuantity))
            throw new InvalidOperationException("A dispatch cannot exceed the confirmed quantity.");
        DispatchNoteNumber = dispatchNoteNumber.Trim();
        DispatchedAt = dispatchedAt;
        Status = ConnectOrderStatus.Dispatched;
    }

    public void MarkReceived() 
    {
        if (Status != ConnectOrderStatus.Dispatched) throw new InvalidOperationException("Only a dispatched order can be received.");
        Status = ConnectOrderStatus.Received;
    }

    private void EnsureHasLines() { if (_lines.Count == 0) throw new InvalidOperationException("An order must contain lines."); }
    private void EnsureOpenForSupplier() { if (Status is not (ConnectOrderStatus.Submitted or ConnectOrderStatus.PartiallyConfirmed)) throw new InvalidOperationException($"Order is {Status}."); }
}

[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class ConnectOrderLine : Entity
{
    private ConnectOrderLine(Guid tenantId, Guid orderId, string sku, string description, Quantity requested, Money unitPrice) : base(tenantId)
    {
        OrderId = orderId; SupplierSku = sku.Trim(); Description = description.Trim(); RequestedQuantity = requested;
        ConfirmedQuantity = Quantity.Zero(requested.UnitOfMeasure); DispatchedQuantity = Quantity.Zero(requested.UnitOfMeasure); UnitPrice = unitPrice;
    }
    private ConnectOrderLine() { }
    public Guid OrderId { get; private set; }
    public string SupplierSku { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Quantity RequestedQuantity { get; private set; }
    public Quantity ConfirmedQuantity { get; private set; }
    public Quantity DispatchedQuantity { get; private set; }
    public Money UnitPrice { get; private set; }
    internal static ConnectOrderLine Create(Guid tenantId, Guid orderId, string sku, string description, Quantity quantity, Money unitPrice)
    {
        if (string.IsNullOrWhiteSpace(sku) || string.IsNullOrWhiteSpace(description) || quantity.Value <= 0 || unitPrice.Amount < 0)
            throw new ArgumentException("Order line is invalid.");
        return new(tenantId, orderId, sku, description, quantity, unitPrice);
    }
    internal void Confirm(Quantity quantity)
    {
        if (quantity.UnitOfMeasure != RequestedQuantity.UnitOfMeasure || quantity.Value < 0 || quantity > RequestedQuantity)
            throw new ArgumentException("Confirmed quantity is invalid.");
        ConfirmedQuantity = quantity;
    }
    internal void Dispatch(Quantity quantity)
    {
        if (quantity.UnitOfMeasure != RequestedQuantity.UnitOfMeasure || quantity.Value < 0 || quantity > ConfirmedQuantity)
            throw new ArgumentException("Dispatched quantity is invalid.");
        DispatchedQuantity = quantity;
    }
}
