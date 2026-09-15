# TASK-27-003 — Store checklist execution metadata

Status: COMPLETE for checklist execution/evidence-authorization slice — period-close acceptance remains a Stage 27 follow-up  
Stage: 27  
Type: Domain

## Objective

Define company/store checklists and immutable execution metadata suitable for offline capture and
safe replay.

## Scope

Checklist definition validation and append-only execution metadata: operation identity, device
identity, capture time, submit time and external evidence reference.

## Acceptance and verification

- Checklist codes and item codes are normalized and unique.
- Execution identities and device/evidence references are required.
- Submission before capture is refused.
- Capture metadata remains unchanged after creation.

2026-09-13: Asset-focused unit suite passes 6/6, including offline replay metadata and timestamp
ordering tests.

## Verification update

2026-09-13: Added tenant/company-scoped EF mappings and `Stage27ChecklistPersistence`, plus
company-guarded checklist create/submit commands and `assets` manage-protected API routes. Execution
replays with matching operation content return the existing identity; changed content is refused.
`ChecklistCommandTests` passes 3/3 and StoreServer Release build passes with 0 errors. Evidence
upload authorization and period-close acceptance remain open.

The replay guard now compares store, device, capture time, submit time and evidence reference in
addition to company and checklist identity; `ChecklistCommandTests` passes 4/4. Altered offline
payloads cannot reuse an operation identity.

Checklist submission now also validates that the loaded checklist belongs to the requested company
and store before creating an execution. `ChecklistCommandTests` passes 5/5, including foreign
checklist refusal.

2026-09-14: Added a view-permission-protected evidence authorization endpoint. It returns a
15-minute opaque HMAC grant only when the execution belongs to the active company and carries an
evidence reference; tampering and expiry are rejected by the authorizer tests (2/2).

## Follow-up findings

local queue APIs, evidence upload/storage integration and period-close acceptance remain open.
