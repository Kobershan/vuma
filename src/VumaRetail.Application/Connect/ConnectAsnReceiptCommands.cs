#pragma warning disable CS1591, IDE0011, CA1062
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Application.Procurement;
using VumaRetail.Domain.Connect;
using VumaRetail.Domain.Procurement;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Connect;

/// <summary>Creates a draft GRN from a dispatched Connect ASN; the retailer still completes it explicitly.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateGoodsReceiptFromConnectAsnCommand(Guid ConnectOrderId, string? DeliveryNoteNumber = null,
    Guid? GoodsReceiptId = null) : ICommand<Guid>;

public sealed class CreateGoodsReceiptFromConnectAsnCommandValidator : AbstractValidator<CreateGoodsReceiptFromConnectAsnCommand>
{
    public CreateGoodsReceiptFromConnectAsnCommandValidator() => RuleFor(x => x.ConnectOrderId).NotEmpty();
}

public sealed class CreateGoodsReceiptFromConnectAsnCommandHandler(
    IConnectOrderRepository connectOrders, IGoodsReceiptRepository receipts, IPurchaseOrderRepository purchaseOrders,
    IDocumentNumberSequence numbers, IPrincipalAccessor principal, ITenantContext tenant, IClock clock)
    : ICommandHandler<CreateGoodsReceiptFromConnectAsnCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateGoodsReceiptFromConnectAsnCommand command, CancellationToken cancellationToken = default)
    {
        ConnectOrder connectOrder = await connectOrders.FindForTenantAsync(command.ConnectOrderId, tenant.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Connect order not found.");
        if (connectOrder.RetailerTenantId != tenant.TenantId || connectOrder.Status != ConnectOrderStatus.Dispatched)
            throw new UnauthorizedAccessException("Only the retailer can receive a dispatched Connect order.");
        PurchaseOrder purchaseOrder = await purchaseOrders.FindAsync(connectOrder.PurchaseOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Purchase order not found.");
        Guid receivedBy = ProcurementActor.RequireUserId(principal);
        Guid receiptId = command.GoodsReceiptId ?? VumaRetail.Domain.Primitives.UuidV7.NewGuid();
        if (command.GoodsReceiptId is not null && await receipts.FindAsync(receiptId, cancellationToken).ConfigureAwait(false) is not null)
            return receiptId;
        GoodsReceipt receipt = GoodsReceipt.Open(purchaseOrder, await numbers.NextAsync("GRN", cancellationToken).ConfigureAwait(false),
            command.DeliveryNoteNumber ?? connectOrder.DispatchNoteNumber, receivedBy, clock.UtcNow, receiptId);
        foreach (ConnectOrderLine line in connectOrder.Lines)
        {
            if (line.PurchaseOrderLineId is null || line.DispatchedQuantity.Value <= 0) continue;
            PurchaseOrderLine purchaseOrderLine = purchaseOrder.RequireLine(line.PurchaseOrderLineId.Value);
            receipt.AddLine(purchaseOrderLine, line.DispatchedQuantity,
                Quantity.Zero(line.DispatchedQuantity.UnitOfMeasure), GoodsRejectionReason.None, "Pre-populated from Connect ASN.");
        }
        if (receipt.Lines.Count == 0) throw new InvalidOperationException("Connect ASN has no mapped dispatched lines.");
        receipts.Add(receipt);
        return receipt.Id;
    }
}
