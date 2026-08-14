using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Platform;

/// <summary>Something platform-owned was asked for that does not exist, or is not this tenant's.</summary>
/// <param name="what">What was being looked for, for example <c>store</c>.</param>
/// <param name="id">The identifier that found nothing.</param>
public sealed class PlatformNotFoundException(string what, Guid id)
    : DomainException("PLATFORM_NOT_FOUND", $"No {what} with id {id}.", DomainProblemKind.NotFound);

/// <summary>Something platform-owned must be unique and is not.</summary>
/// <param name="code">The stable machine-readable code for the specific collision.</param>
/// <param name="message">What collided, in words.</param>
public sealed class PlatformConflictException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Conflict)
{
    /// <summary>A store code already used in this tenant.</summary>
    /// <param name="code">The code.</param>
    public static PlatformConflictException StoreCode(string code)
        => new("PLATFORM_STORE_CODE_TAKEN", $"Store code '{code}' is already used in this tenant.");
}
