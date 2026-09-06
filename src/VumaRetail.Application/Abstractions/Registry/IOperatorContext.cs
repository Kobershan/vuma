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

    /// <summary>
    /// The fingerprint of the licence that carries this operator identity, for acceptance
    /// records. Null until the request edge resolves it (FIX-4 middleware).
    /// </summary>
    string? LicenceFingerprint { get; }

    /// <summary>The acting principal, in the form written to audit rows.</summary>
    string Principal { get; }

    /// <summary>
    /// Requires an active operator context. Throws if the licence has lapsed or no operator
    /// is present.
    /// </summary>
    Guid RequireOperatorId();

    /// <summary>
    /// Binds the acting operator for this request. Called at the request edge from the licence
    /// claims (or explicitly by the seeder); never from business code.
    /// </summary>
    void SetOperator(Guid operatorId, string? operatorName, string? licenceFingerprint, bool isActive);
}
