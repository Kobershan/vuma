using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Loyalty;

/// <summary>The member does not have enough points for the redemption (Stage 20).</summary>
public sealed class InsufficientPointsException()
    : DomainException(
        "INSUFFICIENT_POINTS", "The member does not have sufficient points.");

/// <summary>The points amount is invalid — negative earn, zero burn, or worse (Stage 20).</summary>
public sealed class InvalidPointsException(string detail)
    : DomainException("INVALID_POINTS", detail);

/// <summary>The same idempotency key arrived with a different body (Stage 20).</summary>
public sealed class DuplicateLoyaltyTransactionException()
    : DomainException(
        "LOYALTY_DUPLICATE_KEY",
        "This idempotency key was already used for a different request.",
        DomainProblemKind.Conflict);

/// <summary>The original request is still in flight; the client must wait, not resubmit (Stage 20).</summary>
public sealed class LoyaltyRequestInProgressException()
    : DomainException(
        "LOYALTY_REQUEST_IN_PROGRESS",
        "This request is already being processed. Wait for its outcome rather than resubmitting.",
        DomainProblemKind.Conflict);

/// <summary>The customer is not enrolled as a loyalty member (Stage 20).</summary>
public sealed class LoyaltyMemberNotFoundException()
    : DomainException(
        "LOYALTY_MEMBER_NOT_FOUND",
        "This customer is not enrolled in the loyalty programme.",
        DomainProblemKind.NotFound);

/// <summary>The customer is already enrolled (Stage 20).</summary>
public sealed class LoyaltyMemberAlreadyEnrolledException()
    : DomainException(
        "LOYALTY_ALREADY_ENROLLED",
        "This customer is already enrolled in the loyalty programme.",
        DomainProblemKind.Conflict);

/// <summary>Loyalty is switched off for this company (Stage 20).</summary>
public sealed class LoyaltyDisabledException()
    : DomainException(
        "LOYALTY_DISABLED", "The loyalty programme is not enabled for this company.");

/// <summary>The transaction being acted on does not exist (Stage 20).</summary>
public sealed class LoyaltyTransactionNotFoundException()
    : DomainException(
        "LOYALTY_TRANSACTION_NOT_FOUND",
        "The loyalty transaction does not exist.",
        DomainProblemKind.NotFound);
