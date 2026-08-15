using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Partners;

/// <summary>Something partner-owned was asked for that does not exist, or is not this tenant's.</summary>
/// <param name="what">What was being looked for, for example <c>partner</c>.</param>
/// <param name="id">The identifier that found nothing.</param>
public sealed class PartnerNotFoundException(string what, Guid id)
    : DomainException("PARTNER_NOT_FOUND", $"No {what} with id {id}.", DomainProblemKind.NotFound);

/// <summary>Something partner-owned must be unique and is not.</summary>
/// <param name="code">The stable machine-readable code for the specific collision.</param>
/// <param name="message">What collided, in words.</param>
public sealed class PartnerConflictException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Conflict)
{
    /// <summary>A partner code already used in this tenant.</summary>
    /// <param name="code">The code.</param>
    public static PartnerConflictException PartnerCode(string code)
        => new("PARTNER_CODE_TAKEN", $"Partner code '{code}' is already used in this tenant.");
}

/// <summary>A partner business rule was broken.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What the rule says.</param>
public sealed class PartnerRuleException(string code, string message) : DomainException(code, message)
{
    /// <summary>A partner was given neither the customer nor the supplier flag.</summary>
    public static PartnerRuleException MustBeCustomerOrSupplier()
        => new(
            "PARTNER_TYPE_REQUIRED",
            "A partner must be a customer, a supplier, or both — that is the reason the type exists.");
}
