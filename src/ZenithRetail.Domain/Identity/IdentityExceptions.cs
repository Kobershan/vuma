using ZenithRetail.Domain.Primitives;

namespace ZenithRetail.Domain.Identity;

/// <summary>
/// Something identity was asked to act on does not exist, or is not visible to this tenant.
/// </summary>
/// <remarks>
/// One exception for "not found" and "not yours" on purpose. Tenant isolation is a global query
/// filter, so another tenant's user simply is not there — and telling a caller which of the two it
/// was would turn any id-taking endpoint into an existence oracle across tenants.
/// </remarks>
/// <param name="what">What was being looked for, for example <c>user</c>.</param>
/// <param name="id">The identifier that found nothing.</param>
public sealed class IdentityNotFoundException(string what, Guid id)
    : DomainException("IDENTITY_NOT_FOUND", $"No {what} with id {id}.");

/// <summary>Something must be unique and is not.</summary>
/// <param name="code">The stable machine-readable code for the specific collision.</param>
/// <param name="message">What collided, in words.</param>
public sealed class IdentityConflictException(string code, string message)
    : DomainException(code, message)
{
    /// <summary>A sign-in name already in use in this tenant.</summary>
    /// <param name="userName">The name.</param>
    public static IdentityConflictException UserName(string userName)
        => new("IDENTITY_USER_NAME_TAKEN", $"'{userName}' is already a user name in this tenant.");

    /// <summary>A role name already in use in this tenant.</summary>
    /// <param name="name">The name.</param>
    public static IdentityConflictException RoleName(string name)
        => new("IDENTITY_ROLE_NAME_TAKEN", $"'{name}' is already a role in this tenant.");

    /// <summary>
    /// A PIN already held by another live operator. The message never says who — a PIN pad that
    /// names the colleague you collided with has just told you their PIN is close to yours.
    /// </summary>
    public static IdentityConflictException Pin()
        => new("IDENTITY_PIN_TAKEN", "Another active operator already uses that PIN. Choose a different one.");

    /// <summary>A terminal code already used in this store.</summary>
    /// <param name="code">The code.</param>
    public static IdentityConflictException TerminalCode(string code)
        => new("IDENTITY_TERMINAL_CODE_TAKEN", $"Terminal code '{code}' is already used in this store.");

    /// <summary>The user already holds this role at this scope.</summary>
    public static IdentityConflictException RoleAlreadyAssigned()
        => new("IDENTITY_ROLE_ALREADY_ASSIGNED", "The user already holds that role at that scope.");
}

/// <summary>A credential does not meet the policy.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What is wrong with it.</param>
public sealed class WeakCredentialException(string code, string message) : DomainException(code, message)
{
    /// <summary>The password is shorter than the policy allows.</summary>
    public static WeakCredentialException Password()
        => new(
            "IDENTITY_PASSWORD_TOO_SHORT",
            $"A password must be at least {CredentialPolicy.MinimumPasswordLength} characters.");

    /// <summary>The PIN is not 4–8 digits.</summary>
    public static WeakCredentialException Pin()
        => new(
            "IDENTITY_PIN_MALFORMED",
            $"A PIN is {CredentialPolicy.MinimumPinLength}–{CredentialPolicy.MaximumPinLength} digits.");
}
