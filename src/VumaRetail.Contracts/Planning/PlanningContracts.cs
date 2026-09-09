namespace VumaRetail.Contracts.Planning;

/// <summary>One demand history row, as returned by the API.</summary>
public sealed record DemandHistoryEntryResponse(
    Guid Id,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalQuantity,
    string Uom,
    DateTimeOffset GeneratedAt);

/// <summary>One forecast snapshot, as returned by the API.</summary>
public sealed record ForecastSnapshotResponse(
    Guid Id,
    Guid? ItemId,
    Guid? ItemVariantId,
    Guid LocationId,
    DateOnly ForecastPeriod,
    string ForecastMethod,
    decimal Quantity,
    decimal Mape,
    decimal? Bias,
    string Version,
    DateTimeOffset GeneratedAt,
    Guid? GeneratedBy);

/// <summary>Replenishment parameters with the latest safety calculation, as returned by the API.</summary>
public sealed record ReplenishmentParameterResponse(
    Guid Id,
    Guid CompanyId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    string ForecastMethod,
    decimal ServiceLevelPercent,
    int LeadTimeDays,
    int ReviewPeriodDays,
    string Uom,
    decimal? SafetyStock,
    decimal? ReorderPoint,
    bool? SafetyLowConfidence);

/// <summary>Upserts replenishment parameters for one SKU at one location.</summary>
public sealed record UpsertReplenishmentParametersRequest(
    Guid CompanyId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    string ForecastMethod,
    decimal ServiceLevelPercent,
    int LeadTimeDays,
    int ReviewPeriodDays,
    string Uom);

/// <summary>Sets a month's open-to-buy budget.</summary>
public sealed record SetOpenToBuyBudgetRequest(
    Guid CompanyId,
    int Year,
    int Month,
    string? CategoryCode,
    decimal PlannedAmount,
    string Currency);

/// <summary>Open-to-buy status: planned, committed live, remaining.</summary>
public sealed record OpenToBuyStatusResponse(
    Guid CompanyId,
    int Year,
    int Month,
    decimal Planned,
    decimal Committed,
    decimal Remaining,
    bool OverCommitted,
    string Currency,
    DateTimeOffset AsAt);

/// <summary>One replenishment suggestion, as returned by the API.</summary>
public sealed record ReplenishmentSuggestionResponse(
    Guid Id,
    Guid CompanyId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal SuggestedQuantity,
    decimal? AcceptedQuantity,
    string Uom,
    string Reason,
    string Source,
    Guid? SourceCompanyId,
    Guid? SourceLocationId,
    string Status,
    Guid? DownstreamDocumentId,
    bool OverOpenToBuy,
    DateTimeOffset RaisedAt,
    DateTimeOffset ExpiresAt);

/// <summary>Accepts a suggestion with an amended quantity.</summary>
public sealed record AmendAcceptSuggestionRequest(decimal AmendedQuantity);

/// <summary>One markdown plan line, as returned by the API.</summary>
public sealed record MarkdownPlanLineResponse(
    Guid Id,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal CurrentPrice,
    decimal ProposedDiscountPercent,
    string Currency,
    string? AbcXyz,
    decimal SellThroughPercent,
    decimal DaysOfSupply,
    Guid? PromotionId);

/// <summary>One markdown plan with its lines, as returned by the API.</summary>
public sealed record MarkdownPlanResponse(
    Guid Id,
    string Code,
    string Reason,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    int Version,
    string Status,
    Guid? ApprovalRequestId,
    Guid? PromotionId,
    IReadOnlyList<MarkdownPlanLineResponse> Lines);

/// <summary>One SKU to put on a markdown plan.</summary>
public sealed record MarkdownLineRequest(
    Guid? ItemId,
    Guid? ItemVariantId,
    Guid LocationId,
    decimal ProposedDiscountPercent);

/// <summary>Opens a draft markdown plan.</summary>
public sealed record CreateMarkdownPlanRequest(
    Guid CompanyId,
    string Code,
    string Reason,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    IReadOnlyList<MarkdownLineRequest> Lines);

/// <summary>Applies Stage 05's verdict to a waiting plan.</summary>
public sealed record ApplyMarkdownApprovalRequest(bool Approved);

/// <summary>Amends a live plan by versioning.</summary>
public sealed record AmendMarkdownPlanRequest(string NewCode);

/// <summary>A run outcome, as returned by the API.</summary>
public sealed record PlanningRunResponse(
    int Raised,
    int Expired,
    int Skipped,
    int BackordersReallocated);

/// <summary>A created id, as returned by the API.</summary>
public sealed record PlanningIdResponse(Guid Id);
