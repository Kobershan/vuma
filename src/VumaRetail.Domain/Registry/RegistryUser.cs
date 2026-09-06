using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>
/// The user directory in the registry. One row per human.
/// A user may be granted access to multiple companies under the same Operator ID.
/// </summary>
public sealed class RegistryUser
{
    private RegistryUser() { }

    private RegistryUser(Guid id, Guid tenantId, string login, string displayName, Guid operatorId)
    {
        Id = id;
        TenantId = tenantId;
        Login = Require(login, nameof(login));
        DisplayName = Require(displayName, nameof(displayName));
        OperatorId = operatorId;
        IsEnabled = true;
    }

    /// <summary>The user's registry identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The tenant this user belongs to.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The login name.</summary>
    public string Login { get; private set; } = string.Empty;

    /// <summary>The user's contact details.</summary>
    public string ContactDetails { get; private set; } = string.Empty;

    /// <summary>The operator ID this user is associated with.</summary>
    public Guid OperatorId { get; private set; }

    /// <summary>The user's display name.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Whether this user is enabled.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Creates a new registry user.</summary>
    public static RegistryUser Create(Guid tenantId, string login, string displayName, Guid operatorId, string? contactDetails = "")
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
        if (operatorId == Guid.Empty)
        {
            throw new ArgumentException("An operator identifier is required.", nameof(operatorId));
        }
        return new RegistryUser(UuidV7.NewGuid(), tenantId, login, displayName, operatorId)
        {
            ContactDetails = contactDetails?.Trim() ?? ""
        };
    }

    /// <summary>Disables the user.</summary>
    public void Disable()
    {
        IsEnabled = false;
    }

    /// <summary>Enables the user.</summary>
    public void Enable()
    {
        IsEnabled = true;
    }

    /// <summary>Updates contact details.</summary>
    public void UpdateContactDetails(string details)
    {
        ContactDetails = Require(details, nameof(details));
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}
