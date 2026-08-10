# STAGE 02 — Identity, RBAC, permission catalogue

**Status:** DONE (2026-08-10) · **Depends on:** 01 · **Reference reading:** `CLAUDE.md` §4 (auth row), §7 (rules 1–3, 8, 10, 11, 15), §8, `docs/CONVENTIONS.md` §3–§5, `docs/DECISIONS.md` ADR-008, ADR-010, ADR-013, ADR-021, ADR-026, ADR-028, ADR-034, `docs/TESTING.md` §2, `docs/SECURITY.md` (written by this stage)

## Objective

Answer three questions the rest of the build has been unable to ask: **who is acting**, **which
tenant and store are they acting in**, and **are they allowed to do this**.

Stage 01 left `SystemPrincipalAccessor` stamping `system:host` on every row, which makes R6's audit
trail a log of the process rather than of a person. Stage 03 is about to build the command pipeline
and needs something to authorise against. Every module stage after that declares permissions. All
three depend on this stage and none of them can be built first.

The deliverable is not a login screen. It is the **permission catalogue** (ADR-013) — a
`module.entity.action` string set that each module declares in code and the pipeline enforces — plus
the credentials that resolve a caller into a set of those strings, in three flavours the product
actually needs: a back-office user with a password, a POS operator with a 4–8 digit PIN, and a
terminal with a pinned client certificate.

## Deliverables

**Domain — `identity` module**
- `PermissionKey` — a validated `module.entity.action` value object. A permission is a string
  everywhere else in the system, and a string that is only validated where it is used is a string
  that will eventually be misspelled in a seed script and silently grant nothing.
- `User` — username, display name, email, password hash, security stamp, POS PIN hash, activation,
  and the lockout counters for both credentials. Never a plaintext or reversible secret.
- `Role`, `RolePermission` — a role is a bag of permission keys (ADR-013). Roles carry no behaviour
  and no code ever branches on a role name.
- `UserRoleAssignment` — a user holds a role either tenant-wide or scoped to one store, carried on
  the base entity's own `store_id` rather than a second column that could disagree with it.
- `Terminal` — an enrolled till: code, store, status, pinned certificate thumbprint, device
  fingerprint, last seen.
- `RefreshToken` — a hash of an issued refresh token, its expiry, and what replaced it on rotation.
- `DomainException` — the base `CONVENTIONS.md` §5 requires, carrying a **stable machine-readable
  code**, so an error survives being reworded.

**Application — permissions and use cases**
- `IPermissionCatalogue` / `PermissionCatalogue` / `IModulePermissions` — modules declare their
  permissions; the catalogue is assembled at startup and is the only source of truth for what a
  permission string may be. Granting one that no module declares is refused.
- `PlatformPermissions`, `IdentityPermissions` — the first two declarations.
- Ports: `IPasswordHasher`, `ITokenIssuer`, `IIdentityUnitOfWork`-shaped repositories
  (`IUserRepository`, `IRoleRepository`, `ITerminalRepository`, `IRefreshTokenRepository`).
- Commands (all `[CommandSideEffect(SideEffect.Write)]`, none exempt): `CreateUserCommand`,
  `CreateRoleCommand`, `AssignRoleCommand`, `SetUserPinCommand`, `EnrolTerminalCommand`,
  `ActivateTerminalCommand`, `RevokeTerminalCommand`.
- Query: `GetEffectivePermissionsQuery` — what this user may do in this store, which is what
  `/api/v1/me/permissions` returns and what the Android app drives its navigation from (ADR-013).
- `AuthenticationService` — password sign-in, PIN sign-in, refresh rotation, sign-out, terminal
  authentication. **Deliberately not commands**; see "Business rules".

**Infrastructure**
- EF configurations for the six `identity` tables, the `Identity` migration, and repositories.
- `IdentityPasswordHasher` over ASP.NET Core Identity's `PasswordHasher<T>` (PBKDF2-HMAC-SHA512,
  100 000 iterations, per-secret salt) — used for passwords, PINs and enrolment codes alike.
- `JwtTokenIssuer` — 15-minute access tokens, 30-day rotating refresh tokens (`CLAUDE.md` §4).
- `AddZenithIdentity(...)` DI extension.

**`src/ZenithRetail.Web` (new project)**
- `HttpContextPrincipalAccessor` — the real `IPrincipalAccessor`, registered **before**
  `AddZenithPersistence` so Stage 01's try-add registration steps aside without being edited.
- `TenantResolutionMiddleware` — sets `ITenantContext` from the authenticated principal at the
  request edge.
- `PermissionAuthorizationHandler` + policy provider — `RequirePermission("identity.user.create")`
  on an endpoint, resolved against the catalogue.
- `TerminalCertificateAuthenticationHandler` — authenticates a till by its pinned thumbprint.
- The auth endpoint group: `POST /api/v1/auth/token`, `/auth/refresh`, `/auth/pin`, `/auth/terminal`,
  `POST /api/v1/auth/sign-out`, `GET /api/v1/me/permissions`.

**Host and tooling**
- `ZenithRetail.StoreServer` wired end to end, and `scripts/seed.sh` / `scripts/seed.ps1` building a
  demonstrable tenant: two stores, three roles, an owner, a manager and a cashier with PINs, and an
  enrolled terminal.

**Docs**
- `docs/SECURITY.md` — the credential model, the threat model, the POPIA position (`CLAUDE.md` §9).

## Business rules

- **Authorisation is by permission, never by role name.** Roles are data a customer edits; a code
  path that reads `IsInRole("Manager")` breaks the moment a tenant renames it, and cannot be granted
  to anyone else. There is an architecture test.
- **A permission that no module declares cannot be granted.** The catalogue is assembled from module
  declarations at startup; `GrantPermission` on an undeclared key throws. A typo becomes an error
  rather than a silently empty grant.
- **A role assignment is scoped.** Tenant-wide or one store. A user with `sales.refund.approve` at
  `JHB01` does not have it at `CPT02`, and the effective-permission query is asked per store.
- **Authentication is not a licensed capability.** Sign-in, refresh and terminal authentication are
  an `AuthenticationService`, not `ICommand`s, so they never reach Stage 04b's read-only interceptor.
  ADR-028 guarantees a lapsed tenant keeps full read, report, reprint and export — which is
  unreachable if they cannot log in. Making sign-in a `Write` command and exempting it would instead
  spend one of the three carve-outs the architecture test caps at three (ADR-034).
- **The POS PIN identifies an operator; it does not authenticate a machine.** A 4-digit PIN has
  10 000 combinations and is typed in front of customers. It is only ever accepted over a session
  that is already terminal-authenticated, it is unique among the tenant's live operators, and it
  locks out after five failures.
- **Credentials are hashed, never encrypted and never logged.** Passwords, PINs, refresh tokens and
  terminal enrolment codes are all one-way. The plaintext of an enrolment code and a refresh token is
  returned exactly once, at the moment it is issued.
- **Refresh tokens rotate, and reuse of a rotated token revokes the chain.** A refresh token
  presented after it has already been exchanged means either a replay or a stolen token; both are
  answered the same way — every live token for that user dies (ADR-026's detection posture).
- **A terminal is trusted on enrolment and pinned thereafter.** Enrolment is authorised by a staff
  member and produces a one-time code; the terminal presents that code once with its self-signed
  client certificate; the thumbprint is bound and the code is spent. Afterwards only that thumbprint
  authenticates that terminal. A changed fingerprint is a flag, never an automatic lockout (ADR-026).
- **Nothing is hard-deleted, including a user.** Deactivation ends access; the rows stay for R6.
- **A user's password change rotates their security stamp**, and every refresh token issued before
  the rotation stops working.

## Tests / acceptance

- `PermissionKey` accepts `module.entity.action` and rejects wrong segment counts, empty segments,
  upper case, whitespace and non-identifier characters
- The catalogue refuses a duplicate declaration, refuses an undeclared grant, and reports the module
  that owns each key
- Password sign-in succeeds; a wrong password increments the counter; the fifth failure locks the
  account for the configured window; a correct password inside the window is still refused; the
  counter resets on success
- PIN sign-in resolves the operator from the store and PIN alone; two live operators cannot hold the
  same PIN; an operator with no role in the store cannot sign in at its till; five wrong PINs lock the
  operator without locking their password login
- Refresh rotates: the old token stops working, the new one works, and presenting the old one again
  revokes every live token for that user
- A password change invalidates refresh tokens issued before it
- Effective permissions are the union of tenant-wide roles and roles scoped to the requested store,
  and exclude roles scoped to a different store
- Terminal enrolment → activation → authentication end to end; an expired code, a spent code and a
  revoked terminal are all refused; a changed fingerprint authenticates and raises a flag
- Handler integration tests against real PostgreSQL and real migrations for all seven commands and
  the query (`docs/TESTING.md` §2)
- Every `identity` table passes the `information_schema` conformance sweep from Stage 01
- Migration `Up` on an empty database, `Down` reverses it, `Up` again succeeds
- Architecture: authorisation by role name fails the build; a declared permission that is not
  `module.entity.action` fails the build. Both proven by a deliberate violation.

## Exit checklist

- [x] `dotnet build -c Release` — **0 warnings, 0 errors** solution-wide
- [x] `scripts/test.sh` — **253 passed, 0 failed** (130 unit, 19 architecture, 104 integration),
      identical across two consecutive runs
- [x] Domain + Application line coverage **89.5%** (Domain 91.3%, Application 87.4%) — the §8 bar is 80%
- [x] Six `identity` tables, each carrying the §7 rule 3 columns, asserted against `information_schema`
      by the Stage 01 conformance sweep
- [x] `Identity` migration applies, reverses to nothing and re-applies — run locally exactly as CI's
      `migrate-check` does, and `has-pending-model-changes` reports the model and migrations agree
- [x] A real `IPrincipalAccessor` is in force — asserted against PostgreSQL: an authenticated caller's
      row lands with `created_by = user:{id}` and an audit entry that is not a system action
- [x] Permission catalogue registered, assembled from two module declarations, and closed — a grant
      of an undeclared permission is refused
- [x] Two new architecture tests, each **proven to fail on a deliberate violation** (see below)
- [x] `docs/SECURITY.md` written; `docs/DATA_MODEL.md` extended with §4b and six replication entries
- [x] `scripts/seed.sh` / `scripts/seed.ps1` produce a demo tenant — 2 stores, 3 roles, 3 users,
      22 grants, 1 enrolled terminal — and a second run creates nothing further
- [x] `docs/PROGRESS.md` updated, ADR-038 to ADR-041 appended, committed and pushed

### Enforcement proven, not assumed

Each new rule was broken on purpose and the break confirmed before reverting:

| Rule | Violation introduced | Reported as |
|---|---|---|
| Authorisation by permission, not role name | `user.IsInRole("Manager")` in Web | `DeliberateViolation.cs:5 — public static bool Check(ClaimsPrincipal user) => user.IsInRole("Manager");` |
| A module declares only its own permissions | `identity` declaring `platform.stolen.permission` | `identity: 'platform.stolen.permission' belongs to module 'platform'` |

The role-name rule also caught a genuine false positive on its first run — `DbSet<Role> Roles => …`
matched a bare `Roles =`. The pattern now requires the attribute's opening quote (`Roles = "`),
because a rule that cries wolf on the model gets suppressed, and then it guards nothing.

### Deviations from the plan, and why

- **ASP.NET Core Identity contributes its `PasswordHasher` and nothing else.** `IdentityUser` cannot
  carry the §7 rule 3 columns, cannot be `[Replicated]`, and `UserManager` commits for itself in
  breach of §7 rule 2. Identity's persistence model and Zenith's could not both be obeyed, and the
  persistence contract wins. ADR-038.
- **A new project, `src/ZenithRetail.Web`.** `CLAUDE.md` §5 does not list it. The ASP.NET-specific
  wiring had to live somewhere the store server and cloud API share but the Stage 09 WPF desktop does
  not inherit. ADR-039, in the same spirit as ADR-031.
- **Sign-in is a service, not a command.** Making it a `Write` command would refuse it while a tenant
  is read-only, which breaks ADR-028's promise that a lapsed tenant keeps reading; exempting it would
  spend one of three carve-outs on something that is not a business write. ADR-040.
- **PIN uniqueness is tenant-wide, not per store.** Per store was the first design and breaks the
  week a cashier covers a shift at another branch. Sign-in candidates are still narrowed to the
  terminal's store. ADR-041.

## Explicitly deferred

- **Endpoint polish.** Versioning, `ProblemDetails` shaping and OpenAPI examples are Stage 03's
  deliverable. The endpoints exist and are reachable now because ADR-008 says the API comes first,
  but their error bodies get their final shape one stage later.
- **mTLS transport configuration.** The terminal certificate handler resolves a thumbprint into a
  `Terminal`; which Kestrel port demands a client certificate, and how the certificate reaches the
  handler behind a reverse proxy, is deployment configuration owned by Stage 31's installer.
- **Signing-key management.** The JWT signing key comes from configuration, with a development
  placeholder and a startup refusal to run on it outside Development. Real key custody (KMS/HSM) is
  Stage 04b's, alongside the licence signing key.
- **Cloud-tier identity.** `ZenithRetail.CloudApi` gets the same wiring when Stage 04 gives it
  something to authorise. Nothing here is store-specific.
- **SSO / MFA.** Neither is in `CLAUDE.md` §4. The security stamp and the credential abstraction leave
  room for both; adding them now would be inventing requirements.
