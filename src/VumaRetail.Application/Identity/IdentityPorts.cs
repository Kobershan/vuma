using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Identity;

/// <summary>
/// One-way hashing for every secret a person can present: passwords, POS PINs, enrolment codes.
/// </summary>
/// <remarks>
/// A port rather than a direct call into ASP.NET Core Identity's <c>PasswordHasher</c>, because the
/// Application layer references Domain and nothing else (§7 rule 1) and because the hashing
/// parameters are an operational decision that will be raised over the product's life. What the
/// domain guarantees is that nothing here is reversible; which KDF is used is Infrastructure's.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Hashes a secret.</summary>
    /// <param name="secret">The plaintext. Never stored, never logged.</param>
    /// <returns>An opaque, self-describing hash including its salt and parameters.</returns>
    string Hash(string secret);

    /// <summary>Verifies a secret against a stored hash, in constant time.</summary>
    /// <param name="hash">The stored hash.</param>
    /// <param name="secret">The presented plaintext.</param>
    /// <returns>Whether the secret matches.</returns>
    bool Verify(string hash, string secret);
}

/// <summary>Hashes an opaque token for storage. Distinct from <see cref="IPasswordHasher"/>.</summary>
/// <remarks>
/// A refresh token is 256 bits of entropy this system generated, not something a person chose, so it
/// needs a lookup-able deterministic digest rather than a deliberately slow salted KDF — the sign-in
/// path has to <em>find</em> the row by hash. Using the password hasher here would mean scanning
/// every live token per refresh, which is both slower and worse.
/// </remarks>
public interface ITokenHasher
{
    /// <summary>Produces the deterministic digest stored for a token.</summary>
    /// <param name="token">The token plaintext.</param>
    /// <returns>The digest, as a hex string.</returns>
    string Hash(string token);

    /// <summary>Generates a new cryptographically random token.</summary>
    /// <returns>The plaintext, returned to the caller exactly once.</returns>
    string Generate();
}

/// <summary>An issued access token and how long it is good for.</summary>
/// <param name="Value">The signed JWT.</param>
/// <param name="ExpiresAt">When it stops being accepted.</param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>A company the token holder may act in, and the roles they hold there.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Roles">Comma-separated role names in that company.</param>
public sealed record CompanyMembership(Guid CompanyId, string Roles);

/// <summary>Who an access token is being minted for.</summary>
/// <param name="UserId">The user.</param>
/// <param name="TenantId">The tenant, which the request edge uses to set <c>ITenantContext</c>.</param>
/// <param name="DisplayName">The user's display name, for the UI.</param>
/// <param name="SecurityStamp">The stamp, so a credential change retires the token.</param>
/// <param name="StoreId">The store being acted in, for a store-scoped session.</param>
/// <param name="TerminalId">The terminal, for a POS session.</param>
/// <param name="OperatorId">The vendor-issued Operator ID, when the user is in the registry directory (ADR-127).</param>
/// <param name="Companies">The companies the user may act in, with roles per company (ADR-127).</param>
public sealed record TokenSubject(
    Guid UserId,
    Guid TenantId,
    string DisplayName,
    string SecurityStamp,
    Guid? StoreId = null,
    Guid? TerminalId = null,
    Guid? OperatorId = null,
    IReadOnlyList<CompanyMembership>? Companies = null);

/// <summary>Mints signed access tokens.</summary>
/// <remarks>
/// The token carries identity, not authorisation. Permissions are resolved per request from the
/// catalogue — a permission list baked into a 15-minute token is a 15-minute window in which a
/// revoked permission still works (<c>docs/SECURITY.md</c> §1). Company *membership* is identity,
/// not authorisation — which companies a login may act in (ADR-127) — so it travels in the token
/// while the permissions each membership confers stay resolved per request.
/// </remarks>
public interface ITokenIssuer
{
    /// <summary>Mints an access token.</summary>
    /// <param name="subject">Who the token is for.</param>
    /// <returns>The signed token and its expiry.</returns>
    AccessToken Issue(TokenSubject subject);

    /// <summary>How long an issued refresh token lasts.</summary>
    TimeSpan RefreshTokenLifetime { get; }
}

/// <summary>Registry company membership for one login (ADR-127).</summary>
/// <param name="OperatorId">The operator that owns the login, if it is in the registry directory.</param>
/// <param name="Companies">The companies the login may act in, with roles per company.</param>
public sealed record TokenCompanyEnrichment(Guid? OperatorId, IReadOnlyList<CompanyMembership> Companies)
{
    /// <summary>No registry membership — pre-registry logins keep working exactly as before.</summary>
    public static TokenCompanyEnrichment Empty { get; } = new(null, []);
}

/// <summary>Resolves a login's registry company membership at sign-in time.</summary>
/// <remarks>
/// Optional at the sign-in edge: without a registration the token carries no companies and
/// everything behaves as before the registry directory existed.
/// </remarks>
public interface ITokenCompanyEnricher
{
    /// <summary>Looks up the login in the registry directory.</summary>
    Task<TokenCompanyEnrichment> EnrichAsync(Guid tenantId, string userName, CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes <see cref="User"/> rows.</summary>
/// <remarks>
/// Repositories return tracked entities and never commit. The unit of work is the pipeline's
/// (<c>IUnitOfWork</c>, §7 rule 2) so a handler that changes a user and writes an audit-relevant
/// second row commits both or neither.
/// </remarks>
public interface IUserRepository
{
    /// <summary>Finds a user by id.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<User?> FindAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Finds a user by sign-in name, normalised.</summary>
    /// <param name="userName">The name as typed; normalisation is this method's job.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every active user with a PIN who holds a role in the given store — the candidate set a PIN
    /// sign-in is resolved against.
    /// </summary>
    /// <param name="storeId">The store the terminal is in.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// A PIN sign-in has no user name to look up by, so the candidates have to be enumerated and each
    /// hash checked. Scoping to one store keeps that set to the staff of one shop rather than a chain.
    /// </remarks>
    Task<IReadOnlyList<User>> ListPinOperatorsAsync(Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>Every active user in the tenant who holds a PIN.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// The set a new PIN is checked against for collisions. Uniqueness is tenant-wide rather than
    /// per store: staff move between branches and cover shifts, and a PIN that was unique when it was
    /// set must not become ambiguous because somebody was rostered somewhere else next week.
    /// </remarks>
    Task<IReadOnlyList<User>> ListPinHoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a new user.</summary>
    /// <param name="user">The user to add.</param>
    void Add(User user);
}

/// <summary>Reads and writes roles, their grants and their assignments.</summary>
public interface IRoleRepository
{
    /// <summary>Finds a role by id.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Role?> FindAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Finds a role by name, normalised.</summary>
    /// <param name="name">The name as typed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Role?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>The permissions granted to a role.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<RolePermission>> ListPermissionsAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>The assignments a user holds.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<UserRoleAssignment>> ListAssignmentsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The permission strings a user holds in a store: the union of tenant-wide assignments and
    /// assignments scoped to that store.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="storeId">The store being acted in, or <c>null</c> for a tenant-wide action.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyCollection<string>> ListEffectivePermissionsAsync(
        Guid userId,
        Guid? storeId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a role.</summary>
    /// <param name="role">The role to add.</param>
    void Add(Role role);

    /// <summary>Adds a grant.</summary>
    /// <param name="permission">The grant to add.</param>
    void Add(RolePermission permission);

    /// <summary>Adds an assignment.</summary>
    /// <param name="assignment">The assignment to add.</param>
    void Add(UserRoleAssignment assignment);

    /// <summary>
    /// Removes a role and its grants.
    /// </summary>
    /// <param name="role">The role to remove.</param>
    /// <remarks>
    /// A soft delete, not a hard one. <c>Remove</c> is rewritten into <c>deleted_at</c> by
    /// <c>AuditInterceptor</c> (§7 rule 8), which is where the rule is enforced rather than here.
    /// </remarks>
    void Remove(Role role);

    /// <summary>Removes a grant.</summary>
    /// <param name="permission">The grant to remove.</param>
    void Remove(RolePermission permission);

    /// <summary>Removes an assignment.</summary>
    /// <param name="assignment">The assignment to remove.</param>
    void Remove(UserRoleAssignment assignment);
}

/// <summary>Reads and writes terminals.</summary>
public interface ITerminalRepository
{
    /// <summary>Finds a terminal by id.</summary>
    /// <param name="terminalId">The terminal.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Terminal?> FindAsync(Guid terminalId, CancellationToken cancellationToken = default);

    /// <summary>Finds a terminal by its pinned certificate thumbprint, across every tenant.</summary>
    /// <param name="thumbprint">The SHA-256 thumbprint presented by the client certificate.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// Cross-tenant by necessity: a terminal presenting a certificate is how the tenant is
    /// <em>discovered</em>, so there is no ambient tenant to filter by yet. The implementation opens
    /// an explicit <c>BypassTenantFilter</c> scope so the crossing is visible in the log
    /// (<c>ITenantContext</c>), and matches on a unique 64-character thumbprint.
    /// </remarks>
    Task<Terminal?> FindByThumbprintAsync(string thumbprint, CancellationToken cancellationToken = default);

    /// <summary>The terminals awaiting activation in a store, for matching an enrolment code.</summary>
    /// <param name="storeId">The store.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Terminal>> ListPendingAsync(Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>Finds a terminal by store and code.</summary>
    /// <param name="storeId">The store.</param>
    /// <param name="code">The terminal code.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Terminal?> FindByCodeAsync(Guid storeId, string code, CancellationToken cancellationToken = default);

    /// <summary>Adds a terminal.</summary>
    /// <param name="terminal">The terminal to add.</param>
    void Add(Terminal terminal);
}

/// <summary>Reads and writes refresh tokens.</summary>
public interface IRefreshTokenRepository
{
    /// <summary>Finds a live or spent token by its stored digest.</summary>
    /// <param name="tokenHash">The digest from <see cref="ITokenHasher"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// Returns spent tokens too, on purpose. A token presented after it has been rotated is the
    /// signal that matters (<c>docs/SECURITY.md</c> §1) and cannot be seen if the query hides them.
    /// </remarks>
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Every live token for a user, for a sign-out or a chain revocation.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<RefreshToken>> ListLiveAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Adds a token.</summary>
    /// <param name="token">The token to add.</param>
    void Add(RefreshToken token);
}
