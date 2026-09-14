# TASK-18-003 — NCR/CAPA, certificates, recalls and closure evidence

**Status:** IN_PROGRESS — lifecycle slices, dispatch, tracked shipment and multi-step production lot recall traversal implemented; PostgreSQL genealogy acceptance remains · **Stage:** 18 · **Type:** Domain, API, verification, documentation

## Objective

> **Closure note (2026-09-14):** The lifecycle domain/API and available PostgreSQL/API evidence are
> complete. Specialist-agent review is unavailable in this environment and is recorded as a limitation.

Deliver non-conformance/corrective-action closure, certificate revocation, recall traceability and
shelf-life enforcement, then close the stage with seed, backup and specialist evidence.

## Acceptance criteria

- A recall traces all affected authorized outputs/shipments while excluding another tenant.
- Certificates revoke terminally; expired or revoked certificates cannot authorize quality release.
- A non-conformance cannot close before corrective action and resolution evidence exist.
- All high-risk routes have permission-denial, tenant/company-scope and replay tests.

## Evidence

- Domain unit coverage includes recall trace deduplication/closure, certificate revocation and NCR/CAPA
  lifecycle rules.
- OpenAPI and quality permission integration tests pass; migration Up/Down passes on PostgreSQL.
- Opening a recall automatically imports downstream shipment references from tracked ledger
  movements; the regression is covered by `QualityHoldTests`.
- Production output receipt commands and API requests now carry batch, expiry and serial identity,
  so tracked production-output ledger references are available to recall derivation.
- Production material issue commands now carry batch, expiry and serial identity; recall opening follows
  a named input lot through its production reference, output lot and downstream shipment references,
  while filtering ledger evidence to the active tenant and company.
- Quality write-route company binding is covered by the authorized API suite (3/3 passed).
- The rebuilt focused quality integration suite passes **5/5**; this verifies the current API and
  migration surface but does not replace the still-open dispatch/recall traceability scenarios.

## Closure

The available quality unit, PostgreSQL API, migration and permission evidence covers the implemented
quality surface. PostgreSQL multi-step genealogy execution and specialist-agent review remain open.
