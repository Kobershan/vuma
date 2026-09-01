namespace VumaRetail.Contracts.Warehouse;

/// <summary>Creates a new zone at a Stage 08 location.</summary>
public sealed record CreateZoneRequest(Guid LocationId, string Code, string Name, string Type);

/// <summary>A zone, as returned by the API.</summary>
public sealed record ZoneResponse(Guid Id, Guid LocationId, string Code, string Name, string Type, bool IsActive);

/// <summary>Creates a new bin within a zone.</summary>
public sealed record CreateBinRequest(
    Guid ZoneId, string Code, string Name, string Type, decimal? CapacityQuantity = null, string? CapacityUnitOfMeasure = null);

/// <summary>A bin, as returned by the API.</summary>
public sealed record BinResponse(
    Guid Id, Guid LocationId, Guid ZoneId, string Code, string Name, string Type,
    decimal? CapacityQuantity, string? CapacityUnitOfMeasure, bool IsActive);

/// <summary>A bin's balance for one stock-keeping unit, as returned by the API.</summary>
public sealed record BinStockResponse(Guid BinId, Guid? ItemId, Guid? ItemVariantId, decimal QuantityOnHand, string UnitOfMeasure);

/// <summary>Opens a putaway task for unbinned received stock.</summary>
public sealed record OpenPutawayTaskRequest(
    Guid LocationId, Guid? ItemId, Guid? ItemVariantId, decimal Quantity, string UnitOfMeasure,
    string SourceReferenceType, Guid? SourceReferenceId = null);

/// <summary>Confirms that some or all of a putaway task's remaining quantity was shelved into a bin.</summary>
public sealed record ConfirmPutawayRequest(Guid BinId, decimal Quantity, string UnitOfMeasure);

/// <summary>A putaway task, as returned by the API.</summary>
public sealed record PutawayTaskResponse(
    Guid Id, Guid LocationId, Guid? ItemId, Guid? ItemVariantId, decimal Quantity, string UnitOfMeasure,
    string Status, Guid? SuggestedBinId, Guid? ConfirmedBinId, decimal ConfirmedQuantity, decimal Remaining);

/// <summary>Adds a demand line to an open pick wave.</summary>
public sealed record AddPickTaskRequest(
    Guid? ItemId, Guid? ItemVariantId, decimal RequestedQuantity, string UnitOfMeasure, string OutboundReference);

/// <summary>Confirms a pick against its allocation.</summary>
public sealed record ConfirmPickRequest(decimal PickedQuantity, string UnitOfMeasure);

/// <summary>Packs a picked wave.</summary>
public sealed record PackWaveRequest(int PackageCount, string? Note = null);

/// <summary>Confirms a packed wave's shipment.</summary>
public sealed record ShipWaveRequest(string? Carrier = null, string? TrackingNumber = null);

/// <summary>One pick task within a wave, as returned by the API.</summary>
public sealed record PickTaskResponse(
    Guid Id, Guid? ItemId, Guid? ItemVariantId, decimal RequestedQuantity, string OutboundReference,
    Guid? AllocatedBinId, decimal? AllocatedQuantity, decimal? PickedQuantity, string Status, string UnitOfMeasure);

/// <summary>A pick wave and its lines, as returned by the API.</summary>
public sealed record PickWaveResponse(
    Guid Id, Guid LocationId, string Status, DateTimeOffset? ReleasedAt, DateTimeOffset? PickedAt,
    DateTimeOffset? PackedAt, DateTimeOffset? ShippedAt, IReadOnlyList<PickTaskResponse> Tasks);

/// <summary>A wave's pack record, as returned by the API.</summary>
public sealed record PackTaskResponse(Guid Id, Guid PickWaveId, int PackageCount, string? Note, DateTimeOffset PackedAt);

/// <summary>A wave's shipment confirmation, as returned by the API.</summary>
public sealed record ShipmentConfirmationResponse(
    Guid Id, Guid PickWaveId, string? Carrier, string? TrackingNumber, DateTimeOffset ShippedAt);

/// <summary>Opens a new cycle count.</summary>
public sealed record OpenCycleCountRequest(Guid LocationId, Guid? ZoneId = null, DateTimeOffset? ScheduledAt = null);

/// <summary>Records a count for one bin/stock-keeping-unit pair.</summary>
public sealed record RecordCycleCountRequest(Guid BinId, Guid? ItemId, Guid? ItemVariantId, decimal CountedQuantity, string UnitOfMeasure);

/// <summary>One recorded cycle count line, as returned by the API.</summary>
public sealed record CycleCountLineResponse(
    Guid Id, Guid BinId, Guid? ItemId, Guid? ItemVariantId, decimal SystemQuantity, decimal CountedQuantity, decimal Variance, string UnitOfMeasure);

/// <summary>A cycle count and its lines so far, as returned by the API.</summary>
public sealed record CycleCountResponse(
    Guid Id, Guid LocationId, Guid? ZoneId, string Status, DateTimeOffset? ScheduledAt, DateTimeOffset? FinalizedAt,
    IReadOnlyList<CycleCountLineResponse> Lines);

/// <summary>Moves stock directly from one bin to another within the same location.</summary>
public sealed record MoveBinStockRequest(
    Guid SourceBinId, Guid DestinationBinId, Guid? ItemId, Guid? ItemVariantId, decimal Quantity, string UnitOfMeasure);

/// <summary>A newly created row's id.</summary>
public sealed record WarehouseIdResponse(Guid Id);
