#pragma warning disable CS1591
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>Immutable goods-movement delivery note for a registry transfer.</summary>
public sealed class StockTransferDeliveryNote
{
    private StockTransferDeliveryNote() { }

    private StockTransferDeliveryNote(
        Guid tenantId,
        Guid transferId,
        Guid senderCompanyId,
        Guid receiverCompanyId,
        DateTimeOffset issuedAt,
        string? driverReference)
    {
        if (tenantId == Guid.Empty || transferId == Guid.Empty || senderCompanyId == Guid.Empty || receiverCompanyId == Guid.Empty)
        {
            throw new ArgumentException("A delivery note requires tenant, transfer and company identities.");
        }
        Id = UuidV7.NewGuid();
        TenantId = tenantId;
        TransferId = transferId;
        SenderCompanyId = senderCompanyId;
        ReceiverCompanyId = receiverCompanyId;
        Number = $"DN-{transferId:N}";
        IssuedAt = issuedAt;
        DriverReference = string.IsNullOrWhiteSpace(driverReference) ? null : driverReference.Trim();
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TransferId { get; private set; }
    public Guid SenderCompanyId { get; private set; }
    public Guid ReceiverCompanyId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; private set; }
    public string? DriverReference { get; private set; }
    public List<StockTransferDeliveryNoteLine> Lines { get; private set; } = [];

    public static StockTransferDeliveryNote Create(StockTransferRequest transfer, DateTimeOffset issuedAt, string? driverReference = null)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        if (transfer.Status is not (TransferStatus.Shipped or TransferStatus.InTransit or TransferStatus.Received or TransferStatus.Reconciled))
        {
            throw new InvalidOperationException("A delivery note can only be issued after shipment.");
        }

        StockTransferDeliveryNote note = new(
            transfer.TenantId, transfer.Id, transfer.SenderCompanyId, transfer.ReceiverCompanyId, issuedAt, driverReference);
        foreach (StockTransferLine line in transfer.Lines)
        {
            note.Lines.Add(StockTransferDeliveryNoteLine.Create(
                note.TenantId, note.Id, line.ItemId, line.ItemVariantId, line.Quantity,
                line.UnitOfMeasure, line.SenderLocationId, line.ReceiverLocationId,
                line.BatchReference, line.ExpiryDate, line.SerialNumber));
        }
        return note;
    }
}

public sealed class StockTransferDeliveryNoteLine
{
    private StockTransferDeliveryNoteLine() { }

    private StockTransferDeliveryNoteLine(
        Guid tenantId,
        Guid deliveryNoteId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantity,
        string unitOfMeasure,
        Guid senderLocationId,
        Guid? receiverLocationId,
        string? batchReference,
        DateOnly? expiryDate,
        string? serialNumber)
    {
        Id = UuidV7.NewGuid();
        TenantId = tenantId;
        DeliveryNoteId = deliveryNoteId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Quantity = quantity;
        UnitOfMeasure = unitOfMeasure;
        SenderLocationId = senderLocationId;
        ReceiverLocationId = receiverLocationId;
        BatchReference = batchReference;
        ExpiryDate = expiryDate;
        SerialNumber = serialNumber;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeliveryNoteId { get; private set; }
    public Guid? ItemId { get; private set; }
    public Guid? ItemVariantId { get; private set; }
    public decimal Quantity { get; private set; }
    public string UnitOfMeasure { get; private set; } = string.Empty;
    public Guid SenderLocationId { get; private set; }
    public Guid? ReceiverLocationId { get; private set; }
    public string? BatchReference { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public string? SerialNumber { get; private set; }

    public static StockTransferDeliveryNoteLine Create(
        Guid tenantId,
        Guid deliveryNoteId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantity,
        string unitOfMeasure,
        Guid senderLocationId,
        Guid? receiverLocationId,
        string? batchReference = null,
        DateOnly? expiryDate = null,
        string? serialNumber = null)
        => new(tenantId, deliveryNoteId, itemId, itemVariantId, quantity, unitOfMeasure, senderLocationId, receiverLocationId, batchReference, expiryDate, serialNumber);
}
