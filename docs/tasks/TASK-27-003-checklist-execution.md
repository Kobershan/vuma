# TASK-27-003 — Store checklist execution metadata

Status: IN_PROGRESS — persistence, command handlers, API routes and replay tests implemented; evidence authorization and period-close acceptance remain  
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

## Follow-up findings

local queue APIs, evidence upload/download authorization and period-close acceptance remain open.
