namespace VumaRetail.Contracts.Identity;

/// <summary>Signs in with a user name and password.</summary>
/// <param name="UserName">The sign-in name.</param>
/// <param name="Password">The password.</param>
/// <param name="StoreId">The store to act in, where the caller works in one.</param>
public sealed record SignInRequest(string UserName, string Password, Guid? StoreId = null);

/// <summary>Signs a POS operator in on an already terminal-authenticated session.</summary>
/// <param name="TerminalId">The authenticated terminal.</param>
/// <param name="Pin">The 4–8 digit PIN.</param>
public sealed record PinSignInRequest(Guid TerminalId, string Pin);

/// <summary>Exchanges a refresh token for a new pair.</summary>
/// <param name="RefreshToken">The refresh token issued by the last exchange.</param>
public sealed record RefreshRequest(string RefreshToken);

/// <summary>
/// A signed-in session.
/// </summary>
/// <remarks>
/// The refresh token appears here and nowhere else — it is returned once, at issue, and only its
/// digest is ever stored (<c>docs/SECURITY.md</c> §1). A client that loses it signs in again.
/// </remarks>
/// <param name="AccessToken">The signed JWT to send as <c>Authorization: Bearer</c>.</param>
/// <param name="ExpiresAt">When the access token stops being accepted.</param>
/// <param name="RefreshToken">The refresh token, returned exactly once.</param>
/// <param name="UserId">Who signed in.</param>
/// <param name="DisplayName">Their display name, for the UI.</param>
public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    Guid UserId,
    string DisplayName);

/// <summary>Activates an enrolled terminal.</summary>
/// <param name="StoreId">The store the terminal was enrolled in.</param>
/// <param name="EnrolmentCode">The one-time code.</param>
/// <param name="CertificateThumbprint">SHA-256 thumbprint of the device's client certificate.</param>
/// <param name="DeviceFingerprint">The device fingerprint to record.</param>
public sealed record ActivateTerminalRequest(
    Guid StoreId,
    string EnrolmentCode,
    string CertificateThumbprint,
    string DeviceFingerprint);

/// <summary>What a terminal gets back when it activates.</summary>
/// <param name="TerminalId">The terminal it is now bound to.</param>
public sealed record TerminalActivationResponse(Guid TerminalId);

/// <summary>What the signed-in caller may do in the store they are acting in.</summary>
/// <param name="UserId">The caller.</param>
/// <param name="StoreId">The store the answer is scoped to, or <c>null</c> for tenant-wide.</param>
/// <param name="Permissions">The <c>module.entity.action</c> strings they hold there.</param>
public sealed record PermissionsResponse(Guid UserId, Guid? StoreId, IReadOnlyCollection<string> Permissions);
