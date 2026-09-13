# TASK-18-003 — NCR/CAPA, certificates, recalls and closure evidence

**Status:** IN_PROGRESS · **Stage:** 18 · **Type:** Domain, API, verification, documentation

## Objective

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
- Quality write-route company binding is covered by the authorized API suite (3/3 passed).

## Remaining

End-to-end recall traceability, shelf-life/dispatch integration, backup/seed recording, specialist
review and final Definition-of-Done audit remain open.
