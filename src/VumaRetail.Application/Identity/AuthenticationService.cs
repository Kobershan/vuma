using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Identity;

/// <summary>Why an authentication attempt failed. The caller sees far less than this.</summary>
public enum AuthenticationFailure
{
    /// <summary>It did not fail.</summary>
    None = 0,

    /// <summary>No such user, or the wrong secret. Deliberately one value, not two.</summary>
    InvalidCredentials = 1,

    /// <summary>Too many failures; the credential is locked for now.</summary>
    LockedOut = 2,

    /// <summary>The user has left.</summary>
    Inactive = 3,

    /// <summary>The refresh token is expired, revoked, or retired by a credential change.</summary>
    TokenNotUsable = 4,

    /// <summary>A refresh token was presented after it had already been exchanged.</summary>
    TokenReused = 5,

    /// <summary>The terminal is not enrolled, not activated, or revoked.</summary>
    TerminalNotAuthorised = 6,
}

/// <summary>The outcome of an authentication attempt.</summary>
/// <param name="Failure">Why it failed, or <see cref="AuthenticationFailure.None"/>.</param>
/// <param name="AccessToken">The signed access token, on success.</param>
/// <param name="RefreshToken">The refresh token plaintext — returned once, never stored, on success.</param>
/// <param name="RefreshTokenId">The id of the stored token row, so a rotation can point at it.</param>
/// <param name="UserId">Who signed in, on success.</param>
/// <param name="TenantId">The tenant, on success.</param>
/// <param name="DisplayName">The user's display name, for the UI, on success.</param>
public sealed record AuthenticationResult(
    AuthenticationFailure Failure,
    AccessToken? AccessToken = null,
    string? RefreshToken = null,
    Guid RefreshTokenId = default,
    Guid UserId = default,
    Guid TenantId = default,
    string? DisplayName = null)
{
    /// <summary>True when the attempt produced tokens.</summary>
    public bool Succeeded => Failure == AuthenticationFailure.None;

    /// <summary>A failure carrying no tokens and no identity.</summary>
    /// <param name="failure">Why it failed.</param>
    public static AuthenticationResult Failed(AuthenticationFailure failure) => new(failure);
}

/// <summary>The outcome of authenticating a terminal by its certificate.</summary>
/// <param name="Terminal">The terminal, on success.</param>
/// <param name="Failure">Why it failed, or <see cref="AuthenticationFailure.None"/>.</param>
public sealed record TerminalAuthenticationResult(Terminal? Terminal, AuthenticationFailure Failure)
{
    /// <summary>True when a terminal was resolved and may trade.</summary>
    public bool Succeeded => Failure == AuthenticationFailure.None && Terminal is not null;
}

/// <summary>
/// Signing in: password, POS PIN, refresh rotation, sign-out and terminal certificates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately not commands.</b> Every other write in Vuma is an <c>ICommand</c> so that Stage
/// 04b's read-only interceptor can refuse it during a subscription lapse (ADR-028, ADR-034). Signing
/// in must survive that lapse: ADR-028 promises a lapsed tenant keeps full read, report, reprint and
/// export access, and every one of those is unreachable by somebody who cannot log in. Classifying
/// sign-in as a write and exempting it would work, but it would spend one of the three carve-outs the
/// architecture test caps at three — for something that is not a business write at all. Authentication
/// happens at the edge, before the pipeline; the pipeline governs what an authenticated caller may
/// then do.
/// </para>
/// <para>
/// It commits through <see cref="IUnitOfWork"/> directly for the same reason. The rows it writes are
/// session state: a rotated token, a failure counter, a last-seen stamp.
/// </para>
/// </remarks>
public sealed class AuthenticationService
{
    private readonly IUserRepository _users;
    private readonly ITerminalRepository _terminals;
    private readonly IRefreshTokenRepository _tokens;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenHasher _tokenHasher;
    private readonly ITokenIssuer _issuer;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly CredentialPolicy _policy;
    private readonly ITokenCompanyEnricher? _companyEnricher;

    /// <summary>Creates the service.</summary>
    /// <param name="users">User lookup.</param>
    /// <param name="terminals">Terminal lookup.</param>
    /// <param name="tokens">Refresh token store.</param>
    /// <param name="passwordHasher">Verifies passwords, PINs and enrolment codes.</param>
    /// <param name="tokenHasher">Digests and generates refresh tokens.</param>
    /// <param name="issuer">Mints access tokens.</param>
    /// <param name="unitOfWork">Commits the session state each attempt writes.</param>
    /// <param name="tenant">
    /// The ambient tenant. A password sign-in needs it already set — the store server knows its own
    /// tenant from activation, and the cloud resolves it at the request edge. A refresh or a terminal
    /// certificate <em>discovers</em> it instead, from a credential that is unique installation-wide,
    /// and sets it here so everything downstream is tenant-filtered normally.
    /// </param>
    /// <param name="clock">The only source of time.</param>
    /// <param name="policy">Lockout thresholds. Defaults to <see cref="CredentialPolicy.Default"/>.</param>
    /// <param name="companyEnricher">
    /// Registry company membership for the token (ADR-127). Optional so sign-in keeps working
    /// where no registry is wired; the container supplies the registry implementation and the
    /// direct constructions in tests keep compiling unchanged.
    /// </param>
    public AuthenticationService(
        IUserRepository users,
        ITerminalRepository terminals,
        IRefreshTokenRepository tokens,
        IPasswordHasher passwordHasher,
        ITokenHasher tokenHasher,
        ITokenIssuer issuer,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        IClock clock,
        CredentialPolicy? policy = null,
        ITokenCompanyEnricher? companyEnricher = null)
    {
        _users = users;
        _terminals = terminals;
        _tokens = tokens;
        _passwordHasher = passwordHasher;
        _tokenHasher = tokenHasher;
        _issuer = issuer;
        _unitOfWork = unitOfWork;
        _tenant = tenant;
        _clock = clock;
        _policy = policy ?? CredentialPolicy.Default;
        _companyEnricher = companyEnricher;
    }

    /// <summary>Signs a user in with their password.</summary>
    /// <param name="userName">The sign-in name as typed.</param>
    /// <param name="password">The password.</param>
    /// <param name="storeId">The store the session acts in, where the caller named one.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Tokens, or a failure.</returns>
    public async Task<AuthenticationResult> SignInWithPasswordAsync(
        string userName,
        string password,
        Guid? storeId = null,
        CancellationToken cancellationToken = default)
    {
        User? user = await _users.FindByUserNameAsync(userName, cancellationToken).ConfigureAwait(false);

        if (user?.PasswordHash is null)
        {
            // No such user and wrong password are the same answer. Distinguishing them turns the
            // sign-in endpoint into a user-name oracle.
            return AuthenticationResult.Failed(AuthenticationFailure.InvalidCredentials);
        }

        DateTimeOffset now = _clock.UtcNow;

        if (!user.IsActive)
        {
            return AuthenticationResult.Failed(AuthenticationFailure.Inactive);
        }

        if (user.IsPasswordLockedOut(now))
        {
            return AuthenticationResult.Failed(AuthenticationFailure.LockedOut);
        }

        if (!_passwordHasher.Verify(user.PasswordHash, password))
        {
            user.RecordFailedPasswordAttempt(now, _policy);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return AuthenticationResult.Failed(AuthenticationFailure.InvalidCredentials);
        }

        user.RecordSuccessfulSignIn(now, CredentialKind.Password);

        return await IssueAsync(user, storeId, terminalId: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Signs a POS operator in with their PIN, on a session that is already terminal-authenticated.
    /// </summary>
    /// <param name="terminalId">The authenticated terminal.</param>
    /// <param name="pin">The 4–8 digit PIN.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Tokens, or a failure.</returns>
    /// <remarks>
    /// The PIN carries no user name, so the operator is resolved by checking it against every active
    /// PIN holder at the terminal's store. That is only safe because PINs are unique among live
    /// operators at a store (enforced when a PIN is set) and because the terminal has already proved
    /// which shop it is standing in (<c>docs/SECURITY.md</c> §1).
    /// </remarks>
    public async Task<AuthenticationResult> SignInWithPinAsync(
        Guid terminalId,
        string pin,
        CancellationToken cancellationToken = default)
    {
        Terminal? terminal = await _terminals.FindAsync(terminalId, cancellationToken).ConfigureAwait(false);

        if (terminal is not { CanAuthenticate: true } || terminal.StoreId is not { } storeId)
        {
            return AuthenticationResult.Failed(AuthenticationFailure.TerminalNotAuthorised);
        }

        DateTimeOffset now = _clock.UtcNow;
        IReadOnlyList<User> candidates = await _users.ListPinOperatorsAsync(storeId, cancellationToken).ConfigureAwait(false);

        User? matched = null;

        foreach (User candidate in candidates)
        {
            // Every candidate is checked even after a match, so the time taken does not reveal how
            // far down the staff list the matching operator sits.
            if (candidate.PinHash is not null && _passwordHasher.Verify(candidate.PinHash, pin))
            {
                matched ??= candidate;
            }
        }

        if (matched is null)
        {
            return AuthenticationResult.Failed(AuthenticationFailure.InvalidCredentials);
        }

        if (matched.IsPinLockedOut(now))
        {
            return AuthenticationResult.Failed(AuthenticationFailure.LockedOut);
        }

        matched.RecordSuccessfulSignIn(now, CredentialKind.Pin);
        terminal.RecordAuthentication(deviceFingerprint: null, now);

        return await IssueAsync(matched, storeId, terminalId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records a failed PIN attempt against a named operator, so a PIN pad can lock one out.
    /// </summary>
    /// <param name="userId">The operator the till believes is typing.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// A PIN sign-in that matches nobody cannot increment anybody's counter — there is no operator to
    /// blame — so a till that shows an operator list and then takes a PIN reports the failure here.
    /// Without it, PIN lockout would only ever apply to a PIN that was once correct.
    /// </remarks>
    public async Task RecordFailedPinAttemptAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        User? user = await _users.FindAsync(userId, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return;
        }

        user.RecordFailedPinAttempt(_clock.UtcNow, _policy);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Exchanges a refresh token for a new pair, rotating it.</summary>
    /// <param name="refreshToken">The refresh token plaintext.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A fresh pair, or a failure.</returns>
    public async Task<AuthenticationResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        string hash = _tokenHasher.Hash(refreshToken);
        RefreshToken? stored = await _tokens.FindByHashAsync(hash, cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            return AuthenticationResult.Failed(AuthenticationFailure.TokenNotUsable);
        }

        // The token digest is what identified the tenant. Set it now so every read below is
        // tenant-filtered in the ordinary way rather than through another bypass.
        _tenant.SetTenant(stored.TenantId, stored.StoreId);

        User? user = await _users.FindAsync(stored.UserId, cancellationToken).ConfigureAwait(false);

        if (user is null || !user.IsActive)
        {
            return AuthenticationResult.Failed(AuthenticationFailure.Inactive);
        }

        DateTimeOffset now = _clock.UtcNow;

        // A token presented after it has already been exchanged is either a replay or a theft, and
        // there is no way to tell which at this moment. Both get the expensive answer.
        if (stored.RevocationReason == RefreshTokenRevocation.Rotated)
        {
            await RevokeAllAsync(user.Id, RefreshTokenRevocation.Reused, now, cancellationToken).ConfigureAwait(false);

            return AuthenticationResult.Failed(AuthenticationFailure.TokenReused);
        }

        if (!stored.IsUsable(now, user.SecurityStamp))
        {
            return AuthenticationResult.Failed(AuthenticationFailure.TokenNotUsable);
        }

        AuthenticationResult result = await IssueAsync(
            user,
            stored.StoreId,
            stored.TerminalId,
            cancellationToken,
            commit: false).ConfigureAwait(false);

        stored.Rotate(result.RefreshTokenId, now);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>Signs a user out by revoking every live refresh token they hold.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task SignOutAsync(Guid userId, CancellationToken cancellationToken = default)
        => await RevokeAllAsync(userId, RefreshTokenRevocation.SignedOut, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Authenticates a terminal by the thumbprint of the client certificate it presented.</summary>
    /// <param name="thumbprint">The SHA-256 thumbprint.</param>
    /// <param name="deviceFingerprint">The fingerprint the device reported, if any.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The terminal, or a failure.</returns>
    public async Task<TerminalAuthenticationResult> AuthenticateTerminalAsync(
        string thumbprint,
        string? deviceFingerprint = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbprint);

        Terminal? terminal = await _terminals
            .FindByThumbprintAsync(thumbprint.ToUpperInvariant(), cancellationToken)
            .ConfigureAwait(false);

        if (terminal is not { CanAuthenticate: true })
        {
            return new TerminalAuthenticationResult(null, AuthenticationFailure.TerminalNotAuthorised);
        }

        // The certificate is what identified the tenant and the store. Everything after this point,
        // including the PIN sign-in that normally follows, runs tenant-filtered.
        _tenant.SetTenant(terminal.TenantId, terminal.StoreId);

        terminal.RecordAuthentication(deviceFingerprint, _clock.UtcNow);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new TerminalAuthenticationResult(terminal, AuthenticationFailure.None);
    }

    private async Task<AuthenticationResult> IssueAsync(
        User user,
        Guid? storeId,
        Guid? terminalId,
        CancellationToken cancellationToken,
        bool commit = true)
    {
        string plaintext = _tokenHasher.Generate();

        RefreshToken token = RefreshToken.Issue(
            user.TenantId,
            user.Id,
            _tokenHasher.Hash(plaintext),
            user.SecurityStamp,
            _clock.UtcNow,
            _issuer.RefreshTokenLifetime,
            storeId,
            terminalId);

        _tokens.Add(token);

        // Registry membership rides the token so the edge can refuse a company the token does
        // not carry (ADR-127). Absent enrichment the token is exactly what it was before.
        TokenCompanyEnrichment enrichment = _companyEnricher is null
            ? TokenCompanyEnrichment.Empty
            : await _companyEnricher.EnrichAsync(user.TenantId, user.UserName, cancellationToken).ConfigureAwait(false);

        AccessToken access = _issuer.Issue(new TokenSubject(
            user.Id,
            user.TenantId,
            user.DisplayName,
            user.SecurityStamp,
            storeId,
            terminalId,
            enrichment.OperatorId,
            enrichment.Companies));

        if (commit)
        {
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return new AuthenticationResult(
            AuthenticationFailure.None,
            access,
            plaintext,
            token.Id,
            user.Id,
            user.TenantId,
            user.DisplayName);
    }

    private async Task RevokeAllAsync(
        Guid userId,
        RefreshTokenRevocation reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RefreshToken> live = await _tokens.ListLiveAsync(userId, cancellationToken).ConfigureAwait(false);

        foreach (RefreshToken token in live)
        {
            token.Revoke(reason, now);
        }

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
