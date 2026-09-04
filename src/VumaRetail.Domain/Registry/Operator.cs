using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>
/// The operator identity the vendor issues and signs into the licence.
/// Never created by a tenant command — only projected from the signed licence.
/// </summary>
public sealed class Operator
{
    private Operator() { }

    private Operator(Guid operatorId, string displayName, string licenceFingerprint, Guid tenantId)
    {
        OperatorId = operatorId;
        DisplayName = Require(displayName, nameof(displayName));
        LicenceFingerprint = Require(licenceFingerprint, nameof(licenceFingerprint));
        TenantId = tenantId;
        IsActive = true;
    }

    /// <summary>The vendor-issued operator identifier, e.g. <c>OP-4K2X-9QN7</c>.</summary>
    public Guid OperatorId { get; private set; }

    /// <summary>The operator's display name.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>The fingerprint of the licence that carries this operator identity.</summary>
    public string LicenceFingerprint { get; private set; } = string.Empty;

    /// <summary>The tenant this operator belongs to.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Whether the operator is currently active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Creates an operator projected from a signed licence.</summary>
    public static Operator Create(Guid operatorId, string displayName, string licenceFingerprint, Guid tenantId)
    {
        if (operatorId == Guid.Empty)
        {
            throw new ArgumentException("An operator identifier is required.", nameof(operatorId));
        }
        return new Operator(operatorId, displayName, licenceFingerprint, tenantId);
    }

    /// <summary>Deactivates the operator — used when a licence lapses.</summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>Re-activates the operator — used when a licence is renewed.</summary>
    public void Reactivate()
    {
        IsActive = true;
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}
