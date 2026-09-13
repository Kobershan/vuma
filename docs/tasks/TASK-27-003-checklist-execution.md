# TASK-27-003 — Store checklist execution metadata

Status: IN_PROGRESS  
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

## Follow-up findings

EF mappings/migration, local queue APIs, evidence upload/download authorization, replay idempotency,
checklist submission API and period-close acceptance remain open.
