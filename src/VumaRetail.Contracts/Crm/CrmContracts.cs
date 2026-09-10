namespace VumaRetail.Contracts.Crm;

/// <summary>Captures a lead.</summary>
public sealed record CreateLeadRequest(
    Guid CompanyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? Company,
    string Source,
    Guid? StoreId = null);

/// <summary>Updates a lead's captured details.</summary>
public sealed record UpdateLeadRequest(
    string FirstName,
    string LastName,
    string? Phone,
    string? Company);

/// <summary>Assigns a lead to a user.</summary>
public sealed record AssignLeadRequest(Guid UserId);

/// <summary>Converts a lead into an existing partner.</summary>
public sealed record ConvertLeadRequest(Guid CustomerId);

/// <summary>One lead, as returned by the API.</summary>
public sealed record LeadResponse(
    Guid Id,
    Guid? CompanyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? Company,
    string Source,
    string Status,
    Guid? AssignedTo,
    Guid? CustomerId,
    DateTimeOffset? ConvertedAt);

/// <summary>What converting a lead produced.</summary>
public sealed record LeadConversionResponse(Guid LeadId, Guid CustomerId, DateTimeOffset ConvertedAt);

/// <summary>Opens an opportunity.</summary>
public sealed record CreateOpportunityRequest(
    Guid CompanyId,
    string Title,
    decimal ExpectedAmount,
    string Currency,
    byte Probability,
    Guid? LeadId = null,
    Guid? CustomerId = null,
    string? Description = null,
    DateOnly? CloseDate = null,
    Guid? StoreId = null);

/// <summary>Moves a deal to a new open stage.</summary>
public sealed record MoveOpportunityStageRequest(string Stage, byte Probability);

/// <summary>Wins a deal against a customer.</summary>
public sealed record WinOpportunityRequest(Guid CustomerId);

/// <summary>Loses a deal with a recorded reason.</summary>
public sealed record LoseOpportunityRequest(string LossReason);

/// <summary>One opportunity, as returned by the API.</summary>
public sealed record OpportunityResponse(
    Guid Id,
    Guid? CompanyId,
    string Title,
    string? Description,
    decimal ExpectedAmount,
    string Currency,
    string Stage,
    byte Probability,
    DateOnly? CloseDate,
    string? LossReason,
    Guid? LeadId,
    Guid? CustomerId,
    Guid? AssignedTo);

/// <summary>Logs an interaction.</summary>
public sealed record LogActivityRequest(
    Guid CompanyId,
    string Type,
    string Subject,
    string? Body,
    string Direction = "Outbound",
    int? DurationMinutes = null,
    Guid? LeadId = null,
    Guid? OpportunityId = null,
    Guid? CustomerId = null,
    Guid? StoreId = null);

/// <summary>One activity, as returned by the API.</summary>
public sealed record ActivityResponse(
    Guid Id,
    string Type,
    string Direction,
    string Subject,
    string? Body,
    DateTimeOffset HappenedAt,
    int? DurationMinutes,
    Guid? LeadId,
    Guid? OpportunityId,
    Guid? CustomerId);

/// <summary>Creates a segment.</summary>
public sealed record CreateSegmentRequest(
    Guid CompanyId,
    string Name,
    string Kind,
    string? Description = null,
    string? QueryExpression = null,
    Guid? StoreId = null);

/// <summary>Adds a member to a static segment.</summary>
public sealed record AddStaticMemberRequest(string MemberType, Guid MemberId);

/// <summary>One segment, as returned by the API.</summary>
public sealed record SegmentResponse(
    Guid Id,
    string Name,
    string? Description,
    string Kind,
    string? QueryExpression,
    bool IsActive);

/// <summary>One static membership, as returned by the API.</summary>
public sealed record SegmentMemberResponse(string MemberType, Guid MemberId, DateTimeOffset AddedAt);

/// <summary>Records affirmative consent for one purpose.</summary>
public sealed record GiveConsentRequest(
    Guid CompanyId,
    Guid CustomerId,
    string Type,
    string Source,
    DateTimeOffset? ExpiresAt = null,
    Guid? StoreId = null);

/// <summary>Withdraws consent.</summary>
public sealed record WithdrawConsentRequest(
    Guid CompanyId,
    Guid CustomerId,
    string Type,
    string? Reason = null);

/// <summary>Qualifies a lead out.</summary>
public sealed record DisqualifyLeadRequest(bool Dead = false);

/// <summary>One consent row, as returned by the API.</summary>
public sealed record ConsentResponse(
    Guid CustomerId,
    string Type,
    string State,
    DateTimeOffset? GrantedAt,
    DateTimeOffset? WithdrawnAt,
    DateTimeOffset? ExpiresAt);

/// <summary>One consent state in the 360° view.</summary>
public sealed record ConsentStateResponse(string Type, string State);

/// <summary>The 360° customer view, as returned by the API.</summary>
public sealed record Customer360ViewResponse(
    Guid CustomerId,
    int LeadCount,
    int OpenOpportunityCount,
    decimal OpenOpportunityValue,
    string OpportunityCurrency,
    int ActivityCount,
    IReadOnlyList<string> SegmentNames,
    IReadOnlyList<ConsentStateResponse> Consents,
    DateTimeOffset AsAt);

/// <summary>A created CRM id.</summary>
public sealed record CrmIdResponse(Guid Id);
