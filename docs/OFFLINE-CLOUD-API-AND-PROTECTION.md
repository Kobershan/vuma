# Offline/cloud APIs, Proxima Orbit and code protection

**Status:** recommendations and proposed acceptance criteria, 12 September 2026.
**Baseline:** 45ee679. This document changes no runtime behavior, tenant policy or GitHub setting.
Read with [the audit](REPOSITORY-AUDIT-2026-09-12.md). The operator requested local and cloud
connectivity, mobile APIs, vendor oversight and protection from copying/unauthorized modification.

## 1. Target ownership and connections

| Component | Authority | Connections and failure behavior |
|---|---|---|
| POS/desktop terminal | Durable local capture queue; not a second unrestricted company database authority | Prefer enrolled local StoreServer; during LAN outage use bounded offline capability and queued operations |
| Local StoreServer | Store/company trading commands, stock commitments, receipts and company books | Local PostgreSQL remains usable without internet; outbound sync and remote-command polling use enrolled device credentials |
| Tenant cloud | Tenant identity/directory, authorized read projections, durable remote intents and encrypted backup coordination | Mobile/approved software connects here; a cloud acknowledgement does not mean an offline store executed a command |
| Mobile clients | Cached views and intent capture | Authenticate to tenant cloud or enrolled local endpoint; display freshness and pending state, persist safe queue across restart |
| Proxima Orbit | Its loyalty identity/points ledger | Server-to-server connector; local earns queue, burns require authoritative approval before becoming tender |
| Vendor control plane | Licence issuance, device enrollment, allowed telemetry, fleet/release management and vendor billing | Separate deployment/database/credentials; store reports aggregate counts/health when online; local trading does not wait for heartbeat |

Cross-company operations still use the registry and separate company databases with idempotent saga
legs. Authority must be explicit per data type; “can connect to both local and cloud” must not mean
both may commit the same transaction independently.

### Request and recovery contract

1. Enroll endpoint profiles and bind tenant, company, store, terminal and allowed actions to identity.
   Never silently send a bearer token or queued operation to an arbitrary replacement URL.
2. Capture a UUID operation ID, canonical input hash, original actor, device, business scope,
   capturedAt, expiry and expected entity version before transmitting.
3. The receiving authority validates current scope, stock/credit, entitlements, consent and version.
   It stores outcome/idempotency atomically with business effects and outbox.
4. Cloud-originated store commands return 202 plus operation/status ID while waiting for store
   execution. Distinguish pending, accepted/committed, rejected, expired and compensation-pending.
5. Reconnect uses the same key. A timeout after commit is an unknown result to resolve, not a reason
   to mint a new key or debit again. A changed body under the same key is a conflict.
6. Remote reports include source checkpoints, AsAt, currency, timezone and unavailable contributors.

Default planning limit for queued approvals/checkout intents is 24 hours, revalidated at execution;
domains may require shorter expiries. Monetary/stock commitments cannot be created from stale cloud
projections. Local captures made under valid offline authority require a distinct reconciliation
policy; do not expire an already completed cash sale and lose its audit trail.

### Offline acceptance matrix

| Scenario | Required outcome |
|---|---|
| Internet/cloud down, local LAN/server healthy | Local cash sales, inventory and receipts continue; cloud lag is visible |
| Local server/LAN down, terminal running | Terminal queues only explicitly supported offline operations, survives restart, and later reconciles once |
| Cloud/mobile writes while store offline | Pending intent; no fabricated stock/credit/approval success |
| Orbit down | Queue earns; pending/declined burns are not payment; cash sale remains possible |
| Vendor control plane down | Keep operating under the agreed offline entitlement policy; no outage misclassified as non-payment |
| Two writers after failover | Require authority epoch/fencing and reconciliation; prohibit automatic split-brain writes |
| Store destroyed | Restore registry, company databases, documents, keys and checkpoints to a newly enrolled installation; reconcile and replay |

The current LICENSING Path A says no-contact leads to read-only at 15 days; top-level R9 says an
outage must not restrict. Resolve this contradiction in a superseding ADR before certification.
Suggested product contract: outages alone produce notices and delayed evidence; restrictions follow
a signed known subscription decision with completed dunning. Explicitly document the resulting
offline fraud/detection tradeoff.

## 2. API roadmap

### Tenant cloud and local serving APIs

Create a route/capability manifest for each real host. Include actual login/tenant discovery,
company selection, permissions, dashboard, stock availability, approvals, documents and operation
status. Validate the manifest against generated OpenAPI and the real mobile client.

Separate staff/member/device/vendor identities. Customer APIs must use public DTOs without internal
costs and access only their own records. Require permission and company membership at resource
access and at queued execution; a role name or a supplied company ID is not enough.
[OWASP object authorization guidance](https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/)

Keep service keys off phones/desktops. Short-lived tokens and rotating refresh credentials need
secure storage, expiry handling and revocation. Local/cloud credential trust should use separate
issuers/audiences/keys; distributing a universal symmetric cloud signing secret to stores increases
the impact of a single compromised store.

### Device replication APIs

Use certificate-bound enrolled node identities and a separate service principal scheme. Validate
envelope node/tier/tenant/company/store, operation identity, payload identity and allowed replication
direction before tracking entities. Separate terminal sales replay commands from generic privileged
row replication where possible. Apply operation/byte limits, replay tracking and key rotation.

### Proxima Orbit

See [the Proxima Orbit integration](PROXIMA-ORBIT.md). Preserve IOrbitClient as the integration
boundary and establish the real upstream contract, environment validation, durable idempotency,
timeouts/unknown outcomes, webhook ordering and financial reconciliation.

Provider membership is not a staff or vendor role. Register an explicit provider/programme mapping;
never identify the legal customer merely by a free-form string. Do not embed Orbit credentials or
import private Orbit source into customer builds. The directory added in this audit is documentation,
not a functioning vendor deployment.

### Vendor-facing APIs

Implement the separately hosted Stage 30b APIs already planned in API_CONTROL_PLANE. Minimum
pre-pilot slice: enrolled device authentication, licence issuance/refresh, signed heartbeat, aggregate
metering, integrity evidence, support grants and release status. The full billing/analytics/mobile
console can follow in its dependent tasks, but core identity/security cannot wait until clients ship.

Suggested telemetry fields: operator/tenant/company/node registration IDs, install ID, release ID,
verified manifest hash, monotonic sequence, last cloud acknowledgement, sync/backup status and
whitelisted usage counters. No customer names, employee details, invoices, transaction lines or
arbitrary diagnostics payloads by default. Enforce the allowlist at sender and receiver.

## 3. What protection can and cannot provide

| Desired property | Practical control | Limit |
|---|---|---|
| Nobody can obtain repository source without permission | Private repository, least-privilege collaborators, MFA, audited access and restricted build/artifact access | Anyone legitimately allowed to read source can potentially copy it; Git cannot report every offline copy |
| Nobody can modify main silently | Protected rulesets, reviewed PRs, mandatory checks, signed commits, owner review for sensitive paths and audit notifications | Local edits/clones remain possible; privileged bypass access must be controlled |
| Customers receive only your releases | Signed Windows/Android packages, signed manifests, protected signing service and provenance | Signatures establish origin/integrity; verification must actually be enforced |
| Copied installs are less useful | Vendor-issued scoped entitlements, device enrollment and non-exportable device keys where supported | A powerful local administrator can patch clients or clone some environments |
| Tampering/distribution becomes observable | Signed/chained evidence, cloud checkpoints, repeated-device detection, source/version alerts and reconciliation | An offline or patched client can withhold reports; absence and self-reports are not proof |
| Proprietary algorithms stay private | Keep vendor-only algorithms/signing/billing in vendor-operated services; deliver only the offline core needed to trade | Any logic required offline must exist locally and can be examined |

Microsoft explicitly says strong names are not a security boundary. Obfuscation or native compilation
may increase reverse-engineering effort but cannot promise uncopyable software. Use obfuscation only
after validating WPF reflection/serialization, crash diagnostics and third-party licence compatibility.
[Microsoft strong-name guidance](https://learn.microsoft.com/en-us/dotnet/standard/assembly/strong-named)

### Repository protections to configure

- Confirm private visibility and review collaborators, deploy keys, actions permissions, forks and
  artifact access. These settings were not verified by this audit.
- Protect main and release tags: require review, current checks, signed commits, restricted deletion/
  force pushes, and explicit, audited administrator bypass.
- Add CODEOWNERS for identity, sync, licensing, deployment and release files; ensure rules enforce
  owner review. CODEOWNERS alone is not an access-control boundary.
- Enable available secret scanning/push protection, dependence/advisory monitoring and notifications
  for collaborator, workflow, branch-rule and release changes.
- Store licensing/release keys in an HSM/KMS or protected signing service. Use short-lived build
  credentials where supported, and keep licensing and release keys separate.
- Restrict notifications to authorized recipients and avoid including source/secrets in alerts.
  No notifications or account-setting changes were made during this audit.

GitHub documents protected branch rules and signed commit requirements; feature availability depends
on repository/account configuration. [Protected branches](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches)
and [push protection](https://docs.github.com/en/code-security/how-tos/secure-your-secrets/prevent-future-leaks).

### Release and installation controls

Build from an immutable reviewed commit. Produce a minimal release-content allowlist, dependency
inventory/SBOM, hashes and signed provenance. Sign Windows binaries/installers and update manifests;
sign Android with production keys. Time-stamp signatures and plan rotation/recovery. Verify the
exact downloaded artifact before installation and refuse unexpected rollback/manifest mismatch.
Test the existing WiX/Velopack plan; this audit does not select a new packaging system.

Code signing establishes package provenance/integrity; timestamps maintain verification across
certificate expiry in supported packaging workflows.
[Microsoft package signing](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview).
Build attestations connect binaries/images to their build provenance.
[GitHub artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations).

Run the store service under a restricted account; restrict installation-directory and database access;
separate updater permissions. Those controls constrain ordinary users, not the owner of the operating
system. Never put vendor private keys, production shared secrets, repository history or source
worktrees in a customer package.

### Tamper evidence and response

On startup/update and periodic checks, verify signed manifests and record changes to protected
files/configuration. Chain events with sequence numbers and signed remote checkpoints, store them
durably while offline, and upload on reconnect. Protect device keys with available OS/hardware
capabilities; treat local filesystem hashes as evidence, not unforgeable attestation.

Vendor detection combines observed device identities, monotonic counters, software versions,
duplicate simultaneous activations, missing checkpoints and integrity failures. Use only approved
aggregate or cryptographic identifiers; do not ingest tenant financial records to detect piracy.

A suspicious install enters human review with an evidence timeline. Separate legitimate hardware
replacement, restore, clock correction and network outages from abuse. Use tenant-visible,
time-limited support access when inspecting business data. Do not remotely destroy data or silently
disable trading based solely on suspicion. Disclose telemetry and handle commercial terms through
the owner's approved agreements.

## 4. Delivery gates

1. Repair audit A01–A05 and execute real negative boundary tests.
2. Make actual desktop startup/POS and local/cloud mobile contracts work; prove durable terminal
   offline behavior. Close A06–A09 and currency/date correctness A14.
3. Connect real Orbit through a tested production configuration, reconcile redemptions and prove
   timeout/replay/reversal behavior (A10–A13).
4. Resolve the licensing outage contract and implement the minimum vendor control-plane service.
5. Enforce release signing, security-job dependencies and clean source/build contexts (A17–A18).
6. Upgrade onto a supported runtime before .NET 9 support ends on 10 November 2026, reconciling the
   locked-stack ADR and deployment versions. [Official lifecycle](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
7. Demonstrate backup/restore and replay using explicit pilot RPO/RTO and outage targets. Only then
   certify a release; a complete stage document is not implementation evidence.

No repository setting, runtime enforcement policy, licence text, live vendor connection or
customer-facing restriction has been changed by these recommendations.
