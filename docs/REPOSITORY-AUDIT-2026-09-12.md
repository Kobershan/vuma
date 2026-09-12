# Vuma Retail repository audit — 12 September 2026

**Audited baseline:** `45ee67939879fe407e8bbf6cf0b3419ed2a95b0f` on `main`.
**Scope:** source and configuration review of identity, tenant/company routing, replication, licensing,
loyalty, host composition, dashboard/client flows, build/release controls and roadmap completeness.
**Deliverables:** this audit; [architecture and protection recommendations](OFFLINE-CLOUD-API-AND-PROTECTION.md);
ten missing stage specifications; roadmap links; [Proxima Orbit integration](PROXIMA-ORBIT.md).

## Executive assessment

Do not treat the current repository as a production-ready offline retail product or a protected
commercial distribution yet. The repository has substantial domain, persistence, API and testing
work, but host composition and client/runtime gaps undermine several completion claims.

The first priorities are the replication identity boundary, PIN/device binding, per-company routing,
the desktop startup regression, production loyalty configuration, and signed release controls.
A passing build or text-based architecture test cannot establish those behaviors.

The vendor requirement is achievable as controlled distribution, signed updates, licensed device
enrollment, delayed tamper evidence and a separately secured monitoring service. It is not possible
to guarantee that an administrator who possesses a customer-side binary cannot copy or patch it,
or that an offline clone always reports its existence. Keep vendor signing keys and vendor-only
services off customer devices, and keep the offline trading core locally available.

This is a broad, risk-focused static audit, not an exhaustive line-by-line certification or penetration
test. Runtime exploitability and deployment-specific controls require the acceptance tests below.

## Evidence and limits

- Inspected the current working tree, tracked file inventory, host startup code, relevant handlers,
  adapters, tests, project configuration, CI and documentation.
- Read-only checks found 1,844 tracked files, including 165 files under `.claude/worktrees/`.
- Roadmap matching found nine stage numbers with no document at all: 17, 18, 21, 23, 27, 28, 29, 30,
  31. Stage 22 had a document for a different subject, so ten subject-specific documents were needed.
- XML resource comparison found 14 distinct `Vuma*` resource references in MainWindow that are
  absent from the LightTheme dictionary actually loaded by the app.
- Confirmed merge-conflict markers in `.gitignore`; no `.dockerignore` is present.
- No .NET build/test process was launched in this audit. No production services, database contents,
  live credentials or real Orbit account were accessed. No benchmark, exploit or restore was run.
- Prior-turn builds/test counts are historical evidence only. In particular, the earlier no-build
  architecture run used an older assembly, and the earlier desktop build did not prove startup.
- Local process inventory returned no dotnet/MSBuild/VBCSCompiler entries when checked.
- GitHub visibility, branch rules, collaborator access, secret alerts, deployment TLS and live
  infrastructure were not authenticated/verified. No package advisory scan or full Git-history
  secret scan was executed; absence of a reported secret/CVE is not a clean-bill claim.
- Specialist roles informed the review areas, but no independent subagent panel was executed.

### Severity and confidence

**P0/Critical:** block customer deployment until fixed/tested.
**P1/High:** address before production enablement of the affected capability.
**P2/Medium:** planned correction with a named owner and test.
“Confirmed static” means the code path or configuration was inspected; it does not mean a live
exploit was demonstrated. “Readiness gap” means required delivery is absent/deferred, not that an
existing locked design was violated.

## Findings

### A01 — P0/Critical: replication trusts payload identity separately from batch identity

**Evidence:** [SyncReceiver](../src/VumaRetail.Sync/Protocol/SyncReceiver.cs), especially GuardTenant,
GuardCompany and ApplyAsync; [ReplicaWriter](../src/VumaRetail.Infrastructure/Sync/ReplicaWriter.cs),
NotCopied and CopyProperties; [batch validator](../src/VumaRetail.Sync/Commands/ReceiveSyncBatchCommand.cs);
[AuditStamper](../src/VumaRetail.Infrastructure/Persistence/Interceptors/AuditStamper.cs).
**Confidence:** confirmed static boundary gap; database exploit not executed.

GuardTenant checks the batch tenant, and GuardCompany compares operation metadata to batch metadata.
ReplicaWriter then copies mapped payload properties, including TenantId, CompanyId and insert Id,
without matching them to the batch/operation identity. Its excluded fields are only RowVersion,
SyncState and SyncStamp. The inspected audit stamper does not validate tenant ownership.

**Failure example:** a principal allowed to push for tenant A supplies batch tenant A, but an otherwise
valid new entity payload carrying tenant B. The batch check passes, while entity identity is sourced
from the different payload. A compromised sync credential therefore has a much larger potential
scope than its enclosing batch suggests.

**Recommendation:** validate all identities before tracking/mutating any entity; derive tenant,
company, store and entity ID from validated scope; reject mismatches and forbidden changes. Bind
database routing to that scope. Restrict entity types and directions per enrolled node.
**Acceptance:** cross-tenant/company/ID payloads are rejected before any row, inbox or cursor changes;
run tests against both hosts and real PostgreSQL.

### A02 — P1/High: sync source identity and authority tier are caller-supplied

**Evidence:** [SyncEndpoints](../src/VumaRetail.Web/Sync/SyncEndpoints.cs) ToBatch,
[SyncReceiver](../src/VumaRetail.Sync/Protocol/SyncReceiver.cs) conflict resolution,
[HttpSyncTransport](../src/VumaRetail.Infrastructure/Sync/HttpSyncTransport.cs),
[PermissionAuthorization](../src/VumaRetail.Web/Identity/PermissionAuthorization.cs).
**Confidence:** confirmed static; deployment protections unverified.

SourceNode and SourceKind are parsed from the request. Enum validation confirms a valid label,
not that the sender owns that node/tier. ConflictResolver consumes the supplied tier. The HTTP sender
uses a configured bearer token; the route does not bind node/tier claims or require the terminal
certificate scheme. Ordinary permission resolution also expects a user subject, leaving the
documented dedicated service identity incomplete.

**Failure example:** a credential with batch push permission claims a higher-authority source tier
or another node's identifier, affecting conflict policy and deduplication.
**Recommendation:** enrolled peer identities, certificate-bound credentials, explicit tier/company
allowlists and separate service authorization; compare envelope claims to enrollment.
**Acceptance:** a terminal cannot claim Store/Cloud or another device ID; another company's node is
refused; legitimate node renewal and replay work after an outage.

### A03 — P1/High: PIN sign-in is not bound to the authenticated terminal

**Evidence:** [IdentityEndpoints](../src/VumaRetail.Web/Identity/IdentityEndpoints.cs) SignInWithPinAsync
passes request.TerminalId; [terminal handler](../src/VumaRetail.Web/Identity/TerminalCertificateAuthenticationHandler.cs)
authenticates the certificate's terminal; [AuthenticationService](../src/VumaRetail.Application/Identity/AuthenticationService.cs)
SignInWithPinAsync loads the passed terminal.
**Confidence:** confirmed static.

**Failure example:** a client authenticated as terminal A submits terminal B's ID in the same tenant.
The PIN lookup and token issuance use B's store/terminal, with no equality check at the endpoint.
**Recommendation:** derive terminal ID from the authenticated certificate identity; remove or
strictly compare the body field. Verify store association on every PIN sign-in.
**Acceptance:** terminal A cannot obtain a token attributed to terminal B, even with a valid B-store PIN.

### A04 — P1/High: unmatched PIN attempts have no effective server-side counter

**Evidence:** [AuthenticationService](../src/VumaRetail.Application/Identity/AuthenticationService.cs),
SignInWithPinAsync returns InvalidCredentials when no candidate matches; RecordFailedPinAttemptAsync
is a separate method. The inspected identity endpoints/host add no PIN rate limiter.
**Confidence:** confirmed application-level gap; proxy rate limits unverified.

**Failure example:** an enrolled/compromised terminal repeatedly guesses PINs; unmatched attempts do
not increment an operator counter. Four-digit PINs have a small search space.
**Recommendation:** durable per-device and per-tenant attempt limits, backoff and alerting at the
server, plus named-user limits where applicable; do not rely on the UI to report failed attempts.
**Acceptance:** repeated wrong PINs trigger a measured 429/backoff policy across restarts and cannot
be bypassed by changing the body terminal ID.

### A05 — P1/High: ordinary business routes do not consistently use company database routing

**Evidence:** [Persistence registration](../src/VumaRetail.Infrastructure/DependencyInjection/PersistenceServiceCollectionExtensions.cs)
binds the ordinary DbContext to one connection string and registers an UnconfiguredCompanyConnectionSecretStore;
[RegistryServices](../src/VumaRetail.Infrastructure/Registry/RegistryServices.cs) contains the guarded
CompanyDbContextFactory; [VumaRetailDbContext](../src/VumaRetail.Infrastructure/Persistence/VumaRetailDbContext.cs)
allows all companies when CurrentCompanyId is null and stamps missing company as Guid.Empty;
[DashboardEndpoints](../src/VumaRetail.Web/Dashboard/DashboardEndpoints.cs) uses the ambient DbContext.
**Confidence:** confirmed composition gap; per-route exposure needs a full authorization matrix.

The presence of a company factory does not ensure every repository and scheduled job uses it.
**Failure example:** a normal dashboard request with no company context can aggregate multiple
company rows in the configured database; a company-factory path fails because its secret provider
is deliberately unconfigured. Either behavior falls short of the separate-books product promise.
**Recommendation:** inventory each business entry point and job; bind authorized company context
before repository resolution, configure secret routing, reject missing business scope, and make
cross-company reads explicit. Do not simply remove the filter or reuse the same database credentials.
**Acceptance:** two physical company databases plus registry, distinct users, reads/writes/jobs/
backup/sync all reach only the permitted company.

### A06 — P0 release blocker: desktop theme fix introduces startup/runtime failures

**Evidence:** [MainWindow.xaml](../src/VumaRetail.Desktop/MainWindow.xaml),
[App.xaml](../src/VumaRetail.Desktop/App.xaml), [ThemeManager](../src/VumaRetail.Desktop/ThemeManager.cs),
[LightTheme.xaml](../src/VumaRetail.Desktop/Themes/LightTheme.xaml).
**Confidence:** confirmed static mismatch; UI not launched in this audit.

The window references VumaSurfaceBase/VumaAccent/etc., but the loaded LightTheme/DarkTheme files
define SurfaceBase/AccentPrimary/etc. The alternative Light.xaml/Dark.xaml files carry the new names
but are not the ones loaded. Fourteen distinct Vuma-prefixed references fail this comparison.
PasswordInput also applies Input, a style whose TargetType is TextBox, to a PasswordBox.

**Failure example:** opening MainWindow can fail resolving a resource; after that is fixed, applying
the incompatible password style can also fail. These defects came from the previous theme change.
**Recommendation:** converge generator/resource contracts and loaded dictionaries; create a compatible
PasswordBox style; use dynamic references where switching themes must update live controls.
**Acceptance:** launch the actual built app on Windows, render login, type into both inputs, switch
light/dark, navigate and close. Keep a startup smoke test alongside source scanners.

### A07 — P1/High: token generation is repeatable but semantically incomplete

**Evidence:** [Program.cs](../src/VumaRetail.TokenGenerator/Program.cs) WriteFlat,
[scripts/generate-tokens.ps1](../scripts/generate-tokens.ps1), [tokens.css](../design/tokens.css).
**Confidence:** confirmed static.

WriteFlat emits scalar values only when exclude.Contains(prop.Key), but default exclusions are empty.
Surface/text scalar tokens therefore disappear. There are also two generator implementations and
two WPF naming schemes.
**Failure example:** regenerate successfully twice; both outputs omit expected surface/text colours,
while determinism tests stay green.
**Recommendation:** make one generator authoritative, preserve/version consumer names, and assert
required token keys, values and loaded resource compatibility before updating committed output.
**Acceptance:** every required colour/type/spacing/radius key is emitted, resolves at its consumer and
is byte-stable on Windows/Linux. The previous CSS regeneration fixed drift, not this logic defect.

### A08 — P1/High: clients do not implement the promised terminal offline mode

**Evidence:** [MainWindow.xaml.cs](../src/VumaRetail.Desktop/MainWindow.xaml.cs),
[Flutter client](../mobile/lib/main.dart), [Android API client](../android/app/src/main/java/com/vuma/retail/mobile/VumaApiClient.kt).
**Confidence:** confirmed inspected client paths; not a claim that no offline backend primitives exist.

Desktop and Flutter clients issue live HTTP calls and keep access tokens in memory. The inspected
paths have no durable transaction queue, local sale capture, endpoint authority handling or refresh
rotation. The desktop's New sale navigates to a descriptive module panel rather than a working till.
**Failure example:** loss of LAN/store server prevents a terminal from capturing a sale; loss of
internet with a healthy local server is a different condition and is not sufficient proof.
**Recommendation:** implement explicit store-offline and terminal-offline acceptance separately,
durable SQLite capture, bounded offline authorization, idempotent replay and real POS UI.
**Acceptance:** restart a disconnected terminal after capture, reconnect twice and reconcile cash,
stock, receipts and GL exactly once.

### A09 — P1/High: CloudApi lacks mobile/dashboard/loyalty serving composition

**Evidence:** [CloudApi Program](../src/VumaRetail.CloudApi/Program.cs),
[StoreServer Program](../src/VumaRetail.StoreServer/Program.cs),
[PublicApi project](../src/VumaRetail.PublicApi/VumaRetail.PublicApi.csproj).
**Confidence:** confirmed readiness gap.

Cloud maps identity, companies and sync; it does not map the dashboard, approvals, stock or public
loyalty surfaces expected by clients. PublicApi is a library, not a separately runnable host.
Cloud disallows a pinned tenant, while anonymous password login has no demonstrated tenant
discovery path; the password lookup needs ambient tenant.
**Failure example:** point the mobile dashboard client at CloudApi: the expected dashboard route
is absent. Fresh cloud password sign-in cannot assume the local store's tenant fallback.
**Recommendation:** compose scoped cloud read APIs and a tenant discovery/auth flow; route remote
writes to their owner via durable intents. Do not enable unrestricted direct cloud writes merely
to achieve route parity.
**Acceptance:** actual mobile login, dashboard, stock, approval and member contracts exercised against
the deployed cloud host with two tenants and an offline store.

### A10 — P1/High: Proxima Orbit fake is selected by the production host

**Evidence:** [StoreServer Program](../src/VumaRetail.StoreServer/Program.cs) AddVumaLoyalty(),
[Loyalty DI](../src/VumaRetail.Infrastructure/DependencyInjection/LoyaltyServiceCollectionExtensions.cs),
[HttpOrbitClient options](../src/VumaRetail.Infrastructure/Loyalty/HttpOrbitClient.cs), ADR-153.
**Confidence:** confirmed static; the fake is a documented development deferral.

The default bool selects InMemoryOrbitClient regardless of environment. The host call does not
consume the UseFake configuration field; real Orbit options binding was not found in src.
**Failure example:** configuring real provider credentials still leaves that call on a process-local
ledger, which loses its state on restart.
**Recommendation:** explicit environment/configuration validation, production adapter selection,
typed options binding and live contract evidence. Document the provider as Proxima Orbit.
**Acceptance:** production cannot select fake mode, sandbox earn/redeem/lookup work, and a provider
outage preserves ordinary cash trading.

### A11 — P1/High: Orbit transport exceptions bypass the queued-outage path

**Evidence:** [HttpOrbitClient.SendAsync](../src/VumaRetail.Infrastructure/Loyalty/HttpOrbitClient.cs),
[LoyaltyEarnRedeemCommands](../src/VumaRetail.Application/Loyalty/Commands/LoyaltyEarnRedeemCommands.cs).
**Confidence:** confirmed static.

Handlers queue on OrbitUnavailableException; HTTP non-success responses are wrapped in that type,
but network exceptions, timeout and JSON parse failures are not normalized in the inspected client.
**Failure example:** DNS failure throws HttpRequestException and rolls back the local transaction
instead of committing the queued earning intent promised by the endpoint.
**Recommendation:** classify transient/unknown/permanent outcomes, preserve caller cancellation,
normalize transport failures and retry with the original provider key; do not retry permanent
business refusals forever.
**Acceptance:** DNS failure, timeout after provider success, malformed 200 and 429 each produce the
documented persisted outcome without duplicate value.

### A12 — P1/High: in-memory Orbit idempotency has a check/apply race

**Evidence:** [InMemoryOrbitClient](../src/VumaRetail.Infrastructure/Loyalty/InMemoryOrbitClient.cs)
EarnAsync/RedeemAsync.
**Confidence:** confirmed static interleaving; concurrency reproduction not run.

The result lookup precedes the ledger lock, and publishing the result follows the lock.
**Failure example:** two same-key requests both miss the result cache, sequentially enter the ledger
lock and each mutate the balance; TryAdd only deduplicates the stored answer, not the mutation.
**Recommendation:** place key validation, original-result lookup, ledger mutation and result storage
within one atomic boundary with tenant/member/operation scope and a body hash.
**Acceptance:** synchronize 100 same-key callers at the lookup; exactly one credit/debit and one
consistent returned result. Keep this test for the real vendor contract as well.

### A13 — P1/High: signed loyalty callbacks can overwrite newer state through replay

**Evidence:** [LoyaltyEndpoints](../src/VumaRetail.PublicApi/Loyalty/LoyaltyEndpoints.cs) VerifySignature,
[ProcessLoyaltyWebhookCommand](../src/VumaRetail.Application/Loyalty/Commands/LoyaltyMemberCommands.cs).
**Confidence:** confirmed static.

HMAC verification correctly rejects an empty secret and uses constant-time comparison. However,
the payload lacks a durable event ID/version/freshness rule; the handler overwrites cached balance
and sets synchronization time to now.
**Failure example:** deliver balance 100, then 80, then replay the old signed 100 event. The cache
returns to 100 and appears fresh.
**Recommendation:** provider registration-derived scope, key rotation, signed event identity/time,
durable inbox deduplication and per-member monotonic version checks.
**Acceptance:** old/duplicate/wrong-tenant events cannot change the newest balance or its AsAt.

### A14 — P1/High: dashboard totals misrepresent money and business dates

**Evidence:** [DashboardEndpoints](../src/VumaRetail.Web/Dashboard/DashboardEndpoints.cs),
[desktop DashboardOverview.Currency](../src/VumaRetail.Desktop/MainWindow.xaml.cs).
**Confidence:** confirmed static.

The query sums all non-cancelled order gross amounts across currencies, uses UTC midnight for
“today”, and the desktop labels the sum using a recent order's currency or ZAR. Draft/open orders
are not the same measure as completed POS sales or issued invoices.
**Failure example:** ZAR 100 + USD 10 produces 110 labelled with one currency; a 00:30 Johannesburg
sale belongs to the previous UTC date. A draft order can inflate “Sales today”.
**Recommendation:** specify metrics, group by currency/company and business timezone, separate
order value from recognized/completed sales, include freshness and scope.
**Acceptance:** mixed-currency, midnight, draft/cancelled/returned order, empty-day and stale-cloud cases.

### A15 — P1/High: licensing outage policy contradicts the top-level offline promise

**Evidence:** [EnforcementPolicy.PathA](../src/VumaRetail.Licensing/Enforcement/EnforcementPolicy.cs),
[LICENSING §4](../docs/LICENSING.md), `CLAUDE.md` R1/R9 and §7 rule 15.
**Confidence:** confirmed documentation/product-policy conflict, not a claim the implementation
violates LICENSING §4.

Path A intentionally enters ReadOnly after the configured no-contact interval (documented default
15 days), even without a known lapsed subscription. Top-level wording says network/vendor outages
must not trigger restrictions.
**Failure example:** a paid, previously activated store loses vendor connectivity for 16 days and
can no longer originate sales under the default policy.
**Recommendation:** record a superseding policy ADR with one explicit offline entitlement contract;
test long vendor outages separately from signed, completed-dunning subscription restrictions.
**Acceptance:** virtual-clock tests at 1/14/15/16/45 days and known lapse/unlock/flush scenarios agree
with the same published customer policy. Do not silently edit only tests or thresholds.

### A16 — P1/High: vendor monitoring and anti-distribution are mostly planned

**Evidence:** [Stage 30b](../docs/stages/STAGE-30b-control-plane.md),
[InProcessControlPlane](../src/VumaRetail.Licensing/Control/InProcessControlPlane.cs),
[HttpControlPlaneClient](../src/VumaRetail.Infrastructure/Licensing/HttpControlPlaneClient.cs);
no ControlPlane project in the inspected solution.
**Confidence:** readiness gap.

Signed licence verification and the client port are useful foundations; they do not deliver an
operational vendor console, remote immutable evidence store, production device issuance or billing.
The client explicitly defers mTLS wiring.
**Recommendation:** implement the separate control plane before paid rollout, with vendor MFA,
roles, signed telemetry, enrolled devices, aggregate counts and audited support grants. Deliver the
minimal security/device service before broad fleet/mobile features.
**Acceptance:** demonstrate a clone suspicion reaching the queue, legitimate hardware replacement
not being auto-disabled, and 24-hour vendor outage not stopping local trading.

### A17 — P1/High: releases are unsigned/debug-signed and security scan is not a package dependency

**Evidence:** [CI](../.github/workflows/ci.yml) package job and needs list;
[Android build](../mobile/android/app/build.gradle.kts) release signing uses debug.
**Confidence:** confirmed repository configuration; external distribution controls unverified.

Windows CI explicitly defers signing. The Android release uses a debug key. Package depends on
tests/design/migrations but not vulnerability-scan, so scan failure need not prevent artifact upload.
**Recommendation:** private release signing, timestamped Windows signatures, Android release keys,
signed update manifests, rollback controls, SBOM/provenance and an explicit security gate.
**Acceptance:** a failing advisory/signature check blocks release publication; modified artifacts
fail installer/updater verification. Git commit signatures alone do not sign installed software.

### A18 — P1/High: repository/build context includes unintended working copies

**Evidence:** `git ls-files '.claude/worktrees/**'` returned 165 paths;
[.gitignore](../.gitignore) lines 84–90 include unresolved merge markers;
[Dockerfile.cloudapi](../Dockerfile.cloudapi) has COPY . . and no .dockerignore exists.
**Confidence:** confirmed static inventory.

**Failure example:** a build context includes local agent copies/history or configuration that is
unnecessary for the product; broad source scanners may see duplicates/stale code. Ignore rules do
not untrack already committed files. A multi-stage final image is not proof that build contexts and
caches never received sensitive files.
**Recommendation:** resolve the ignore conflict, review/remove accidental tracked copies via a
separate approved cleanup, audit their history for secrets, add a minimal Docker context and release
content allowlist. Preserve operator files; do not recursively delete .claude.
**Acceptance:** tracked inventory and container/release manifests exclude all unintended source,
credentials and local caches. No secret value is reproduced in this report.

### A19 — P2/Medium: runtime lifecycle and deployment versions need reconciliation

**Evidence:** [global.json](../global.json), [Directory.Build.props](../Directory.Build.props),
[Dockerfile](../Dockerfile.cloudapi), [render.yaml](../render.yaml), `CLAUDE.md` locked stack.
**Confidence:** confirmed configuration and checked official lifecycle.

.NET 9 is STS, despite the “LTS-track” label. Microsoft's current policy gives its support end as
10 November 2026; schedule migration to a supported LTS through a superseding stack ADR before
that date. The Render database specifies PostgreSQL 17 while the locked baseline says 16; either
verify and document 17 or align environments. [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)

InvariantGlobalization is also enabled solution-wide although requirements specify tenant cultures.
Inventory culture-sensitive parsing/rendering before claiming configurable localization.
**Acceptance:** patched supported runtime, aligned CI/deployment/DR database versions, and en-ZA/
non-default currency/date formatting tests.

### A20 — P2/Medium: guard tests have misleading coverage boundaries

**Evidence:** [DesignSystemRulesTests](../tests/VumaRetail.ArchitectureTests/DesignSystemRulesTests.cs)
ScanFiles; [ContrastAndTypographyTests](../tests/VumaRetail.ArchitectureTests/ContrastAndTypographyTests.cs);
[CURRENT](../docs/CURRENT.md) versus roadmap and previous CI failure.
**Confidence:** confirmed static.

The CSS-named rule also scans XAML/C#/Kotlin/XML under src, but does not include .css in that source
extension filter; separate design scanning covers design CSS. Desktop colour scans cannot validate
resource lookup or type-compatible styles. Assertions about one old compiled assembly do not prove
the current source's complete suite.
**Recommendation:** correct scanner scopes/names, add semantic token/UI tests, and attach each
verification result to commit/configuration/platform/test filter with no stale “all green” claims.
**Acceptance:** deliberately faulty CSS, missing theme key and incompatible PasswordBox style each
fail the appropriate focused test.

### A21 — P1/High readiness: backup labels/checksums do not prove per-company disaster recovery

**Evidence:** [SyncServiceCollectionExtensions.AddVumaBackup](../src/VumaRetail.Infrastructure/DependencyInjection/SyncServiceCollectionExtensions.cs)
constructs the backup engine with the host connection string;
[BackupService](../src/VumaRetail.Infrastructure/Backup/BackupService.cs) labels snapshots with ambient
company and verifies stored-object checksum.
**Confidence:** confirmed wiring; actual deployment manifests/DR unverified.

**Failure example:** selecting company B must not produce a B-labelled backup of the default host
database. A valid encrypted checksum can still belong to the wrong company or contain a database
that cannot be restored with the available keys/schema.
**Recommendation:** derive engine target from the validated company manifest and secret provider;
include registry, every company's database, documents, keys and sync checkpoints in isolated DR.
**Acceptance:** destroy only a disposable installation, restore to fresh hardware and reconcile
company identity, stock/GL totals, document access and replay. State actual RPO/RTO.

### A22 — P2/Medium: configurable bundle output can recursively delete an unchecked directory

**Evidence:** [build-windows-server-bundle.ps1](../scripts/build-windows-server-bundle.ps1) joins a
caller-controlled OutputPath then recursively removes it without containment validation.
**Confidence:** confirmed static; script not executed.

**Failure example:** an erroneous parent-relative OutputPath resolves outside the intended artifacts
directory and can target unrelated data.
**Recommendation:** resolve and validate the final absolute directory, reject repository/home/root/
ancestor paths and require a designated artifact child. Prefer temporary staging and atomic replace.
**Acceptance:** parent traversal, absolute broad paths and symlink/junction escape cases refuse
before deletion; ordinary artifact rebuild succeeds.

## What is already useful

- Shared versioned API mapping, ProblemDetails, declarative permissions and per-request permission
  resolution are good foundations; retain them and test actual host composition.
- Domain ledgers, company saga/availability work and transactional outbox/inbox are the right places
  to build accounting/offline guarantees; strengthen their scope and concurrency boundaries.
- Ed25519 licence documents, emergency-code concepts and aggregate telemetry limits are valuable.
  Keep vendor keys isolated and reconcile outage policy before production.
- Orbit is already separated behind IOrbitClient. HMAC verification now fails closed when the
  webhook secret is missing; do not regress that check.
- CI contains meaningful architecture and migration checks. Add the missing release/security/runtime
  gates rather than equating test quantity with acceptance.

## Recommended execution order

| Priority batch | Owner role | Work | Proof required |
|---|---|---|---|
| 1 — before customer use | API/security + persistence | A01–A05 replication, device identity, PIN limits, company routing | Real two-tenant/two-company negative and replay tests |
| 2 — restore client correctness | Desktop/design + reporting | A06–A09, A14 | Windows startup/POS smoke, semantic tokens, actual cloud/mobile flow |
| 3 — loyalty production gate | Integration + finance | A10–A13 and Stage 20 redemption accounting deferral | Real Orbit sandbox, duplicate/timeout/reversal tests and accounting reconciliation |
| 4 — commercial release controls | Platform/security | A15–A18, A21 | One outage policy, isolated control plane, signed release, clean package and real DR |
| 5 — continuous maintenance | Build/docs | A19–A20, A22 and stage evidence | Runtime upgrade plan, accurate guards, safe tooling, fresh results |

Do not introduce anti-debugging, forced shutdown or automatic deletion as a substitute for these
controls. Commercial/IP terms should be finalized by the owner's counsel; the repository audit
does not establish ownership rights, enforceability or compliance certification.

## Roadmap/documentation work delivered

Created stage specifications for 17 Manufacturing, 18 Quality, 21 Ecommerce, 22 Marketing,
23 Service, 27 Assets/Maintenance/Operations, 28 Projects/Contracts, 29 Reporting, 30 Android,
31 Hardening/DR/Release. Added a shared requirements document and the missing API_ECOMMERCE
contract. Linked existing and new stage titles in ROADMAP.

Stage 22's historical hierarchy/transfers document and its tasks remain intact. The new Marketing
document uses 22M planning identifiers so existing TASK-22-* work is not reinterpreted. Stage 22b's
transport dependency is explicitly the Marketing document. No completed stage is recertified and
no new implementation task is marked READY.

Proxima Orbit now has an explicit vendor integration directory and production-readiness contract.
No real upstream source or credentials were invented/imported. Recommendations for code protection,
offline authority and vendor telemetry are in the companion document.

## Audit follow-up and recheck

Documentation verification completed on 2026-09-12: 20 added/modified Markdown files checked,
204 relative file links resolved, all 47 roadmap stage rows linked, and all ten new specifications
contained the required sections and NOT_STARTED status. No conflict markers or trailing whitespace
were introduced in these documents; `git diff --check` passed. Link checks validate file existence,
not external URLs or every heading anchor. Final process inventory found no dotnet/MSBuild/
VBCSCompiler process. These checks are not application, security exploit or deployment tests.

Treat each finding as a focused repair task with its own evidence. Once corrected, rerun the relevant
runtime/security scenario against both intended hosts, then the current full suite on the supported
Windows/Linux environments. A production audit still needs GitHub configuration, package advisories,
history secret scanning, penetration tests and a witnessed restore/replay exercise.
