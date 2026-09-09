using System;

namespace VumaRetail.Contracts.Warehouse;

public sealed record BuildConsolidatedWaveRequest(
    Guid LocationId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string GeographyLevel,
    string GeographyValue,
    Guid? CompanyScopeId = null);

public sealed record CreateCountScheduleRequest(
    string Name,
    string Cadence,
    string Scope,
    int SlowMoverDays,
    int RandomSampleSize,
    DateTimeOffset NextRunAt);

public sealed record PreviewConsolidatedWaveResponse(
    Guid LocationId,
    string GeographyLevel,
    string GeographyValue,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    IReadOnlyList<GroupedLinePreview> GroupedLines,
    IReadOnlyList<OrderBreakdownPreview> OrderBreakdowns);

public sealed record GroupedLinePreview(
    Guid ItemId,
    Guid? ItemVariantId,
    string UnitOfMeasure,
    string PackSize,
    decimal TotalQuantity,
    int OrderCount);

public sealed record OrderBreakdownPreview(
    Guid OrderId,
    Guid OrderLineId,
    Guid ItemId,
    decimal Quantity);

public sealed record ConsolidatedWaveResponse(
    Guid Id,
    string Status,
    string GeographyLevel,
    string GeographyValue,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    IReadOnlyList<ConsolidatedWaveLineResponse> Lines,
    IReadOnlyList<OrderBreakdownResponse> Breakdowns);

public sealed record ConsolidatedWaveLineResponse(
    Guid? ItemId,
    Guid? ItemVariantId,
    string UnitOfMeasure,
    string PackSize,
    decimal TotalQuantity,
    int OrderCount);

public sealed record OrderBreakdownResponse(
    Guid OrderId,
    Guid OrderLineId,
    decimal Quantity);

public sealed record CountScheduleResponse(
    Guid Id,
    string Name,
    string Cadence,
    string Scope,
    int SlowMoverDays,
    int RandomSampleSize,
    DateTimeOffset NextRunAt,
    bool IsActive);

public sealed record CountScheduleSummary(
    Guid Id,
    string Name,
    string Cadence,
    string Scope,
    int SlowMoverDays,
    int RandomSampleSize,
    DateTimeOffset NextRunAt,
    bool IsActive);

public sealed record CountSheetResponse(
    Guid ScheduleId,
    Guid LocationId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<CycleCountSummary> Counts,
    IReadOnlyList<InFlightWarning> InFlightWarnings);

public sealed record CycleCountSummary(
    Guid CycleCountId,
    string Scope,
    string Status,
    DateTimeOffset ScheduledAt);

public sealed record InFlightWarning(
    Guid BinId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal InFlightQuantity,
    string WaveReference);

// Stage 13 original warehouse types
public sealed record CreateZoneRequest(
    Guid LocationId,
    string Code,
    string Name,
    string Type,
    bool IsActive = true);

public sealed record ZoneResponse(
    Guid Id,
    Guid LocationId,
    string Code,
    string Name,
    string Type,
    bool IsActive);

public sealed record CreateBinRequest(
    Guid LocationId,
    Guid ZoneId,
    string Code,
    string Name,
    string Type,
    decimal? CapacityValue = null,
    string? CapacityUnitOfMeasure = null,
    bool IsActive = true);

public sealed record BinResponse(
    Guid Id,
    Guid LocationId,
    Guid ZoneId,
    string Code,
    string Name,
    string Type,
    decimal? CapacityValue,
    string? CapacityUnitOfMeasure,
    bool IsActive);

public sealed record MoveBinStockRequest(
    Guid SourceBinId,
    Guid DestinationBinId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure,
    Guid? TransferId = null);

public sealed record OpenPutawayTaskRequest(
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure,
    string SourceReferenceType,
    Guid SourceReferenceId,
    Guid? SuggestedBinId = null);

public sealed record ConfirmPutawayRequest(
    Guid PutawayTaskId,
    Guid ConfirmedBinId,
    decimal ConfirmedQuantity,
    string UnitOfMeasure);

public sealed record PutawayTaskResponse(
    Guid Id,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string Status,
    Guid? SuggestedBinId,
    Guid? ConfirmedBinId,
    decimal ConfirmedQuantity,
    decimal Remaining);

public sealed record AddPickTaskRequest(
    Guid PickWaveId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure,
    string OutboundReference,
    Guid? PickTaskId = null);

public sealed record ConfirmPickRequest(
    Guid PickTaskId,
    decimal Quantity,
    string UnitOfMeasure);

public sealed record PackWaveRequest(
    Guid PickWaveId,
    int PackageCount,
    string? Note = null);

public sealed record ShipWaveRequest(
    Guid PickWaveId,
    string? Carrier = null,
    string? TrackingNumber = null);

public sealed record OpenCycleCountRequest(
    Guid LocationId,
    Guid? ZoneId,
    DateTimeOffset? ScheduledAt = null);

public sealed record RecordCycleCountRequest(
    Guid CycleCountId,
    Guid BinId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal CountedQuantity,
    string UnitOfMeasure);

public sealed record BinStockResponse(
    Guid BinId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal QuantityOnHand,
    decimal QuantityReserved,
    decimal Available);

public sealed record PickWaveResponse(
    Guid Id,
    Guid LocationId,
    string Status,
    DateTimeOffset? ReleasedAt,
    DateTimeOffset? PickedAt,
    DateTimeOffset? PackedAt,
    DateTimeOffset? ShippedAt,
    IReadOnlyList<PickTaskResponse> Tasks);

public sealed record PickTaskResponse(
    Guid Id,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal RequestedQuantity,
    string OutboundReference,
    Guid? AllocatedBinId,
    decimal? AllocatedQuantity,
    decimal? PickedQuantity,
    string Status);

public sealed record CycleCountResponse(
    Guid Id,
    Guid LocationId,
    Guid? ZoneId,
    string Status,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? FinalizedAt,
    IReadOnlyList<CycleCountLineResponse> Lines);

public sealed record CycleCountLineResponse(
    Guid Id,
    Guid BinId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal SystemQuantity,
    decimal CountedQuantity,
    decimal Variance);

public sealed record WarehouseIdResponse(Guid Id);

public sealed record PackTaskResponse(
    Guid Id,
    Guid PickWaveId,
    int PackageCount,
    string? Note,
    DateTimeOffset PackedAt);

public sealed record ShipmentConfirmationResponse(
    Guid Id,
    Guid PickWaveId,
    string? Carrier,
    string? TrackingNumber,
    DateTimeOffset ShippedAt);