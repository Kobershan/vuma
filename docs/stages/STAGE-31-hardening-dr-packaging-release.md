# STAGE 31 — Hardening, Disaster Recovery, Packaging and Release

**Status:** NOT_STARTED — specification created 2026-09-12, implementation not certified · **Depends on:** all in-scope stages, including 30b; release-blocking audit findings closed · **Reference reading:** [audit](../REPOSITORY-AUDIT-2026-09-12.md), [protection plan](../OFFLINE-CLOUD-API-AND-PROTECTION.md), [sync/backup](../SYNC_AND_BACKUP.md) §§8–12, [licensing](../LICENSING.md); [shared stage requirements](STAGE-SHARED-REQUIREMENTS.md) §§1–5; [execution standard](../EXECUTION_STANDARD.md) Part 1; `CLAUDE.md` §§3,7–8. New names and defaults below are proposed implementation contracts, not claims that types/routes already exist.

## Objective

Produce a signed, recoverable release with measured security, availability and offline behavior. Prove trading from local infrastructure, controlled cloud/mobile access and vendor monitoring without making vendor uptime a transaction dependency.

## What this stage does not own

This stage verifies and packages module behavior; it does not substitute a passing build for runtime acceptance or rewrite unresolved financial/product policy silently.

## Deliverables

### Domain and client model

Proposed contracts: `ReleaseManifest`, `UpdatePolicy`, `InstallationInventory`, `RestoreVerificationResult`, `IntegrityReport`. Reuse Stage 30b's device/update contracts and existing backup models. Keep release manifests and verification artifacts in operator tooling under `scripts/` and `deploy/`; do not create a business-domain Hardening module or company tables merely for release metadata.

### Application

Installer/update/restore operations use explicit operator tooling; vendor rollout approvals remain Stage 30b commands. Proposed ports where host integration requires them: `IReleaseVerifier`, `IUpdateSource`, plus existing backup/restore and control-plane ports. Keep business posting and approvals in their owning modules; release tooling must not mutate their tables directly.

### Infrastructure

Implement package/signature verification, staged atomic activation, rollback recovery and database-aware restore adapters under existing deployment, desktop updater and Infrastructure boundaries. Reuse Stage 30b inventory persistence; no new schema is assumed. If restore/update state needs persistence, document its owner and include migration/recovery tests. Do not replicate release metadata as company business records.

### API

Consume versioned device/update APIs from Stage 30b; liveness/readiness endpoints disclose no secrets; administrative restore is separately authorized, never public. Publish OpenAPI examples, permissions, idempotency, concurrency and error codes for actual host routes. Restore tooling is operator-facing, not a new customer PublicApi surface. Preserve route compatibility where an endpoint exists already.

### Permissions and entitlement

Separate release signer, rollout approver, restore operator and fleet viewer permissions; map them to Stage 30b's actual permission names before implementation. Do not grant these rights to a member/mobile credential or introduce a paid Hardening module. Enforce company scope on backup/restore, whitelist aggregate metering, and retain maintenance/read/export licensing carve-outs.

## Business rules

1. Windows releases and update manifests are signed and timestamped; Android uses a protected release key. Repackaged or rollback artifacts fail verification.
2. Build/release credentials are separate from licensing keys; signing keys stay outside source and shipped binaries.
3. Minimal release contents exclude source checkouts, .git, private keys, local config and build caches; SBOM and provenance bind to exact artifact hashes.
4. Company/registry restore uses a manifest of database identity, schema version, keys, documents and replay checkpoint; checksum success alone is not DR success.
5. Acceptance targets for the defined pilot dataset: online RPO ≤15 minutes, full rebuild RTO ≤4 hours, 20 tills × 1 completed cash sale/second for 30 minutes with zero lost/duplicate postings. Offline loss exposure is stated separately.
6. Security tests include terminal binding, cross-tenant/company payloads, replay, secret rotation, missing configuration, interrupted updates and vendor outage. No automatic destructive enforcement.
7. Unsigned local patches, delayed telemetry and missing heartbeats are evidence with uncertainty; they are not guaranteed proof of piracy.
8. Apply the shared requirements in §§1–5 to every entry point; authorization happens again when a queued intent executes.

## Parts — the build list

- [ ] 31-P01: Close critical/high audit findings and execute host/auth/replication/offline acceptance.
- [ ] 31-P02: Package Windows Service/desktop and Android; sign manifests/binaries, publish SBOM/provenance and test updates.
- [ ] 31-P03: Run isolated disaster recovery, load/security tests, operations rehearsal and go/no-go review.

Execute parts in this order. These are stage parts, not existing canonical task files. Before implementation, decompose each part into focused tasks using [the full task template](../tasks/README.md), name exact existing source/test paths, and link them from a canonical stage queue. No implementation task is marked READY by this documentation change. Record any durable change to existing architecture as a superseding/proposed ADR.

## Tests / acceptance

- `Store_survives_cloud_outage`: block cloud and vendor network for 24 hours; local cash trading completes and reconnect replay changes no totals.
- `Fresh_hardware_restore_trades`: restore the pilot registry, all company databases and documents onto a fresh machine within 4 hours; reconcile stock/GL and complete a sale.
- `Tampered_update_is_refused`: flip one byte in a release package; the updater refuses it and records the reason without deleting tenant data.
- `Interrupted_update_recovers`: stop power between staging and activation; restart chooses a verified working release.
- `Signing_failure_blocks_release`: unavailable signing service or missing security result produces no distributable production artifact.
- `Other_tenant_and_unauthorized_company_are_denied`: authenticated tenant A/company A cannot read, mutate, export or enqueue for tenant B/company B by changing an ID.
- `Replay_with_different_content_is_rejected`: reuse a completed operation ID with changed input; return a stable conflict and preserve the original result.
- Execute migration Up/Down on a disposable database, permission-denial tests on every high-risk route and module read-only behavior. Client-only changes mark database checks not applicable with a reason.

## Exit checklist

- [ ] Every listed rule and scenario has executed evidence, including outage/replay and authorization.
- [ ] Planned API routes are verified against the actual host's OpenAPI and real client contracts.
- [ ] Per-company accounting/stock, retention and audit requirements are satisfied where applicable.
- [ ] Seed/demo, migration reversibility, backup implications and module replication registration are evidenced.
- [ ] Relevant specialist reviews from [AGENTS](../AGENTS.md) are recorded; missing tooling is UNVERIFIED, not an invented review.
- [ ] `CLAUDE.md` §8 is met, measured results are recorded and unresolved release blockers remain open.

**Verification boundary:** this document was reviewed for scope and links only. No stage implementation, live API, UI, migration or production vendor integration was certified in the 2026-09-12 audit.
