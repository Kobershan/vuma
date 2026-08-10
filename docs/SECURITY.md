# SECURITY — Vuma Retail

Written in Stage 02, which built identity, and extended by every stage that adds a trust boundary.

Vuma runs on hardware the customer owns, in a shop, on a LAN a technician set up. That shapes every
decision here: the threat is rarely a sophisticated remote attacker and usually a shared password, a
till left logged in, a laptop plugged into the store switch, or a backup on an unencrypted disk.

---

## 1. Principals

Four kinds of thing act in Vuma, and they authenticate differently because they are different.

| Principal | Credential | Audit form | Lives |
|---|---|---|---|
| **Back-office user** | username + password → JWT | `user:{id}` | Cloud and store server |
| **POS operator** | 4–8 digit PIN, on an already-authenticated terminal | `user:{id}` + `terminal:{id}` | Store server |
| **Terminal** | pinned X.509 client certificate | `terminal:{id}` | Store server |
| **System** | none — in-process | `system:{component}` | Everywhere |

`IPrincipalAccessor` reports all four in one shape, which is what lets Stage 01's audit interceptor
stamp `created_by` without any module passing an identity down the call stack (R6).

### Passwords

Hashed with ASP.NET Core Identity's `PasswordHasher<T>` — PBKDF2-HMAC-SHA512, 100 000 iterations,
128-bit per-password salt, version-prefixed so the parameters can be raised later and existing hashes
rehashed on next sign-in. Minimum length 12, no composition rules and no forced expiry: both push
users towards `Password1!` and a sticky note on the monitor.

Five consecutive failures lock the account for 15 minutes. The counter resets on success. Sign-in
never distinguishes "no such user" from "wrong password" in its response.

### The POS PIN, and what it is not

A 4-digit PIN has 10 000 combinations and is typed in front of a queue. It is not a password and is
never treated as one:

- It is only accepted on a session that is **already terminal-authenticated**. The certificate proves
  which till; the PIN says who is standing at it.
- It is unique among the tenant's live operators, so the operator is resolvable from store + PIN with
  no operator code to type — the reason tills use PINs at all. Uniqueness is tenant-wide rather than
  per store because staff move between branches and cover shifts: a PIN that was unique when it was
  set must not become ambiguous because somebody was rostered somewhere else next week.
- Sign-in candidates are narrowed to the operators who hold a role in *that* store, so a cashier at
  one branch cannot sign in at another's till even with a valid PIN.
- Five failures lock the operator's PIN for 15 minutes. Their password login is unaffected, and their
  password login cannot be attacked through the PIN.
- It is hashed with the same hasher as a password. There is no reversible PIN store, so no support
  process can ever read one back to a caller.

### Terminals

Enrolment is authorised by a staff member holding `identity.terminal.enrol` and produces a one-time
code with a short expiry. The terminal generates its own key pair and self-signed client certificate,
presents it once with the code, and the server pins the thumbprint. From then on only that thumbprint
authenticates that terminal — trust on first enrolment, pinned afterwards, no CA to run and no
private key ever leaving the till.

A device fingerprint is recorded alongside. A fingerprint that changes still authenticates and raises
a flag for the abuse queue: ADR-026 is explicit that detection never auto-disables anything, because
a false positive that closes a till costs more than the piracy it prevents.

### Tokens

| Token | Lifetime | Storage |
|---|---|---|
| Access (JWT, HS256) | 15 minutes | Never persisted; carried in `Authorization: Bearer` |
| Refresh | 30 days, rotating | Only its SHA-256 hash is stored |

The access token carries `sub`, `tenant`, optional `store`, `terminal`, `stamp` (the user's security
stamp) and `name`. **It does not carry permissions.** A permission list in a 15-minute token is a
15-minute window in which a revoked permission still works, and a token that grows with every module.
Permissions are resolved per request from the catalogue.

Refresh rotates on every use. Presenting an already-exchanged refresh token means a replay or a theft,
and both get the same answer: every live token for that user is revoked. Changing a password rotates
the security stamp, which invalidates every token issued before it.

The JWT signing key comes from configuration. The development placeholder is refused at startup
outside the Development environment — a shipped default signing key is a master key for every
installation that ever used it.

---

## 2. Authorisation

Permissions are `module.entity.action` strings declared in code by the module that owns them
(ADR-013). Roles are bags of permissions; users hold roles either tenant-wide or scoped to one store.

Three rules make this hold:

1. **No code branches on a role name.** Roles are customer-editable data. An architecture test fails
   the build on `IsInRole` or `[Authorize(Roles = …)]`.
2. **A permission that no module declares cannot be granted.** The catalogue is the closed set; a
   typo is an error, not a grant that silently does nothing.
3. **Effective permissions are always asked per store.** The union of tenant-wide roles and roles
   scoped to the store being acted in — never the union across all stores.

Tenant isolation is not part of this: it is a global query filter in the persistence layer
(`docs/DATA_MODEL.md` §1), so a missing permission check leaks a capability, never another tenant's
data.

---

## 3. Transport and network

- Store LAN traffic is HTTPS. The installer provisions a certificate (Stage 31); a self-signed one
  pinned by the terminals is acceptable on a LAN and is the default.
- Terminal → store server uses mutual TLS on a dedicated port. Which port and how the client
  certificate survives a reverse proxy is deployment configuration.
- Store server → cloud is mTLS plus a JWT (`CLAUDE.md` topology). The store's certificate identifies
  the installation; the JWT identifies the tenant.
- The public API (ADR-021) is a separate host with its own auth model and its own DTOs. It shares no
  credential store with staff identity, and no internal contract type is reachable from it.

---

## 4. Data protection

- **At rest.** PostgreSQL data files rely on the host's disk encryption, which the installer requires
  and verifies. Cloud backup snapshots are encrypted before they leave the store (Stage 04), so the
  object store never holds plaintext.
- **Secrets in configuration.** `appsettings.Development.json` is git-ignored; `.env.example` carries
  placeholders. No real credential is ever committed (`CLAUDE.md` §1).
- **Logs.** Two independent controls, because a credential in a log file is a credential in every
  backup of that log file.
  1. **Nothing logs a credential in the first place.** The plaintext of a password, a PIN, a refresh
     token or an enrolment code never leaves the method it arrives in, and the command pipeline logs
     a message's *type name* only — never its payload, which for `CreateUserCommand` is a password.
  2. **The sink refuses to write one anyway.** `RedactingEnricher` (Stage 03) masks any property
     whose name contains `password`, `pin`, `token`, `secret` or `certificate`, case-insensitively,
     at any depth in a log event — inside captured objects, dictionaries and sequences alike. It
     over-redacts by design: a property called `TokenCount` becomes `***`, and that is the correct
     direction for the trade.

  The second control exists because the first is a discipline and disciplines lapse on a Friday
  afternoon. EF Core's statement logging is capped at `Warning` so a busy till does not bury the one
  line anybody wants; parameter values were never logged, sensitive-data logging being off.
- **Nothing is hard-deleted** (§7 rule 8), which is a security property as much as an audit one: a
  deleted user's history remains attributable.

---

## 5. POPIA

South Africa's Protection of Personal Information Act is the default legal frame (`CLAUDE.md` §9),
and the design position is that Vuma holds personal information **on the tenant's behalf** — the
tenant is the responsible party, Vuma the operator.

| POPIA condition | How it is met |
|---|---|
| Accountability | The tenant owns the data; the vendor's access is covered by §6 below |
| Processing limitation | Modules collect what they need for the transaction and no more |
| Purpose specification | Retention periods are per-entity configuration, defaulting to the 5 years tax law requires |
| Further processing | Marketing (Stage 22) requires a recorded consent per channel, and honours withdrawal |
| Information quality | Customers can be corrected; corrections are audited, never silent |
| Openness | The audit trail (R6) answers "who saw or changed my record" |
| Security safeguards | This document |
| Data subject participation | Export and erasure requests are a first-class operation in Stage 19 (CRM): export produces the subject's full record; erasure pseudonymises rather than deletes, because financial documents are immutable (ADR-012) and the tax record must survive |

**Erasure and immutability.** A data-subject erasure request cannot delete a posted invoice. The
implemented position is pseudonymisation: identifying fields on the customer record are replaced,
the transaction history keeps its totals and its legal integrity, and the link back to a person is
gone. This is recorded here rather than decided per module.

**Cross-border.** A tenant whose cloud tier is hosted outside South Africa needs §72 grounds. The
cloud tier's region is tenant configuration for that reason, not a vendor-wide constant.

---

## 6. Vendor access

R10 and ADR-024 are the controlling requirements, and they are strict on purpose:

- Telemetry leaving a tenant's premises for vendor purposes is **whitelisted aggregate counters only**.
  No customer names, no sales line detail, no employee data, no document content. There is a test.
- Vendor staff have **no standing path to tenant business data**. Support access requires a
  tenant-granted, time-boxed grant, is dual-audited, and shows a banner inside the tenant's own UI
  for as long as it is open.
- The control plane is a separate deployment with its own database and credentials (Stage 30b). A
  compromise there exposes usage metadata, not anybody's trading data.

---

## 7. What this stage does not cover

SSO, MFA, hardware security keys and password-less sign-in are all absent, deliberately —
`CLAUDE.md` §4 does not ask for them and the security stamp plus the credential abstraction leave
room to add any of them later. Key custody (KMS/HSM) for the JWT and licence signing keys is Stage
04b's. Encrypted backup and the restore path are Stage 04's, hardened in Stage 31.
