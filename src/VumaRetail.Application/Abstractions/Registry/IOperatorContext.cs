using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>
/// Resolves the acting Operator ID from the signed licence claims.
/// </summary>
public interface IOperatorContext
{
    /// <summary>The operator ID of the person acting on this request.</summary>
    Guid? OperatorId { get; }

    /// <summary>The operator's display name.</summary>
    string? OperatorName { get; }

    /// <summary>Whether the operator is currently active.</summary>
    bool IsActive { get; }

    /// <summary>The acting principal, in the form written to audit rows.</summary>
    string Principal { get; }

    /// <summary>
    /// Requires an active operator context. Throws if the licence has lapsed or no operator
    /// is present.
    /// </summary>
    Guid RequireOperatorId();
}
