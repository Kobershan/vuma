# TASK-007 — Company connection resolver and cache invalidation

## Objective
Resolve company IDs to registry-owned connection secret references through a safe, invalidatable cache.
## Why
Adding or moving a company must not require redeployment or configuration edits.
## Scope
`ICompanyConnectionResolver`, registry lookup, cache entries/expiry, explicit invalidation and refresh-on-change seam.
## Out of scope
DbContext creation, provisioning, secret encryption provider implementation, API endpoints, and fan-out.
## Affected files/components
Application connection port; infrastructure resolver/cache; registry outbox/event seam; unit/integration tests.
## Dependencies
TASK-005 and TASK-006; ADR-118; `docs/MULTI_COMPANY.md` §2 and security rules.
## Relevant references
Stage 06c; `docs/DATA_MODEL.md` §4l; `docs/SECURITY.md` if named by implementation; `docs/EXECUTION_STANDARD.md`.
## Implementation requirements
Never read company connections from appsettings; cache only resolved encrypted references/usable secret material according to the security design; invalidate on registry changes, support bounded staleness, and reject missing/inactive companies.
## Acceptance criteria
Resolver returns the current registry value; invalidation prevents stale routing after change; concurrent misses are bounded; secrets do not appear in logs or support output.
## Tests
Cache hit/miss/expiry/invalidation tests; inactive/missing company tests; concurrent refresh test; secret-redaction test.
## Edge cases
Registry outage with warm cache, changed secret, revoked company, cache stampede, malformed reference.
## Security considerations
Use least privilege, zero plaintext persistence, redacted diagnostics, and no telemetry containing credentials.
## Architectural decision impact
Implements ADR-118 and supports ADR-099 physical isolation; record any cache freshness exception as an ADR.
## Definition of done
Resolver seam and tests are green, reviewed, documented, and scoped findings are closed.
## Status
COMPLETE
## Work log
- 2026-08-27: Planned as the routing slice before context creation.
