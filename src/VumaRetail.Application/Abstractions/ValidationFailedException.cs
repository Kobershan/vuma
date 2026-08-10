using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Abstractions;

/// <summary>
/// A message was refused by its validator before it reached its handler.
/// </summary>
/// <remarks>
/// <para>
/// Mapped to <c>400</c>, not <c>422</c>. The distinction is worth keeping: a validation failure means
/// the request was malformed and the client can fix it from the response alone, whereas a
/// <see cref="DomainException"/> means the request was well formed and the business rejected it —
/// the same PIN, resubmitted, will fail again for a reason no amount of reformatting cures.
/// </para>
/// <para>
/// It derives from <see cref="DomainException"/> so that a caller who only knows about domain errors
/// still gets a stable code, and so that the exception handler has one base type to reason about.
/// </para>
/// </remarks>
public sealed class ValidationFailedException : DomainException
{
    /// <summary>The stable code clients branch on.</summary>
    public const string ErrorCode = "VALIDATION_FAILED";

    /// <summary>Creates the exception.</summary>
    /// <param name="messageName">The command or query that was refused.</param>
    /// <param name="errors">Failures grouped by the property that produced them.</param>
    public ValidationFailedException(string messageName, IReadOnlyDictionary<string, string[]> errors)
        : base(ErrorCode, $"{messageName} is not valid.", DomainProblemKind.Malformed)
    {
        ArgumentNullException.ThrowIfNull(errors);

        Errors = errors;
    }

    /// <summary>
    /// The failures, grouped by property name, in the shape ASP.NET Core's validation problem
    /// details expects.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
