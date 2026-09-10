using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Crm;

/// <summary>A lead that already converted cannot convert again (Stage 19).</summary>
public sealed class LeadAlreadyConvertedException()
    : DomainException(
        "LEAD_ALREADY_CONVERTED",
        "A lead that has already been converted cannot be converted again.");

/// <summary>A terminal lead cannot be re-opened (Stage 19).</summary>
public sealed class LeadTerminalStateException(string detail)
    : DomainException("LEAD_TERMINAL_STATE", detail);

/// <summary>Another live lead already holds this email in this store (Stage 19).</summary>
public sealed class DuplicateLeadEmailException(string email)
    : DomainException(
        "LEAD_DUPLICATE_EMAIL",
        $"A lead with email '{email}' already exists in this store.",
        DomainProblemKind.Conflict);

/// <summary>The lead being acted on does not exist, or is not this tenant's (Stage 19).</summary>
public sealed class LeadNotFoundException()
    : DomainException("LEAD_NOT_FOUND", "The lead does not exist.", DomainProblemKind.NotFound);

/// <summary>The partner a conversion points at does not exist in this company (Stage 19).</summary>
public sealed class LeadCustomerNotFoundException()
    : DomainException(
        "LEAD_CUSTOMER_NOT_FOUND",
        "The customer this lead converts into does not exist in this company.",
        DomainProblemKind.NotFound);

/// <summary>A won opportunity without a customer (Stage 19).</summary>
public sealed class OpportunityMissingCustomerException()
    : DomainException(
        "OPPORTUNITY_MISSING_CUSTOMER",
        "A won opportunity must reference the customer it converted into.");

/// <summary>An opportunity marked lost without a reason (Stage 19).</summary>
public sealed class OpportunityLossReasonRequiredException()
    : DomainException(
        "OPPORTUNITY_LOSS_REASON_REQUIRED",
        "An opportunity cannot be marked lost without recording why.");

/// <summary>An illegal opportunity stage transition (Stage 19).</summary>
public sealed class OpportunityStageTransitionException(string detail)
    : DomainException("OPPORTUNITY_BAD_TRANSITION", detail);

/// <summary>The opportunity being acted on does not exist (Stage 19).</summary>
public sealed class OpportunityNotFoundException()
    : DomainException(
        "OPPORTUNITY_NOT_FOUND", "The opportunity does not exist.", DomainProblemKind.NotFound);

/// <summary>An activity record was mutated or deleted (Stage 19).</summary>
/// <remarks>
/// ADR-012 shape: activities are append-only compliance records (POPIA §9). The persistence
/// layer also refuses updates to <c>IImmutableRecord</c>; this guards the domain path.
/// </remarks>
public sealed class ActivityImmutableException()
    : DomainException(
        "ACTIVITY_IMMUTABLE",
        "An activity record is immutable and cannot be modified or deleted.");

/// <summary>A segment cannot be both static and dynamic (Stage 19).</summary>
public sealed class SegmentMixedKindException()
    : DomainException(
        "SEGMENT_MIXED_KIND", "A segment cannot be both static and dynamic.");

/// <summary>A dynamic segment was written to directly (Stage 19).</summary>
public sealed class DynamicSegmentWriteNotAllowedException()
    : DomainException(
        "SEGMENT_DYNAMIC_WRITE_NOT_ALLOWED",
        "Dynamic segments are evaluated at read time; members cannot be added directly.");

/// <summary>The segment being acted on does not exist (Stage 19).</summary>
public sealed class SegmentNotFoundException()
    : DomainException(
        "SEGMENT_NOT_FOUND", "The segment does not exist.", DomainProblemKind.NotFound);

/// <summary>A consent record already exists for this customer and type (Stage 19).</summary>
public sealed class DuplicateConsentException()
    : DomainException(
        "CONSENT_DUPLICATE",
        "A consent record already exists for this customer and type.",
        DomainProblemKind.Conflict);

/// <summary>Marketing was attempted without valid consent (Stage 19).</summary>
public sealed class ConsentNotGivenException(ConsentType type)
    : DomainException(
        "CONSENT_NOT_GIVEN",
        $"No valid {type} consent exists for this customer.");
