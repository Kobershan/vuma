# Task

## Status

NOT_STARTED

## Stage

Stage 20 — verification + public contract doc

## Type

TESTING, DOCUMENTATION

## Objective

Prove TASK-20-001 meets stage acceptance + §8: suites green on real PG, arch green,
coverage ≥80%, migration Down verified, PublicApi OpenAPI complete, `docs/API_LOYALTY.md`
written as the authoritative public contract, seed + PROGRESS evidence.

## Why

Stage 21 builds on member identity; the public contract must be written, not implied.

## Scope

- Contract/API test suite for PublicApi loyalty surface.
- Coverage measurement + gap closure for Loyalty namespaces.
- `docs/API_LOYALTY.md` (endpoint shapes, auth, idempotency, rate limits, webhooks,
  error codes) — derived from the implemented surface, not the stage doc's draft.
- DATA_MODEL.md §loyalty, SYNC_AND_BACKUP.md registry rows, seed (demo member + tier +
  reward + earn tx), PROGRESS.md evidence, CURRENT.md handoff.

## Out of Scope

Storefront API (Stage 21); load budgets (Stage 31).

## Architecture

Contract tests assert wire shapes only (no business-table fields leak into public DTOs —
structural check).

## Dependencies

TASK-20-001 COMPLETE.

## Relevant Files

`src/VumaRetail.PublicApi/`, `tests/VumaRetail.IntegrationTests/Api/` (if harness
exists; else new `LoyaltyApiTests` on `WebApplicationFactory`).

## Relevant Documentation

TESTING.md §1, API_STANDARDS.md §10, stage doc §Testing summary.

## Implementation Requirements

- Metering-whitelist test: serialised loyalty rollup matches strict schema (counts only).
- Neutral-503 body scan: no billing/subscription/licence words.

## Data/Database Impact

Seed only.

## API Impact

Docs only.

## Security

Public-DTO leak test: response JSON contains no cost/margin/supplier/other-customer
fields.

## Multi-Company/Tenant Impact

Cross-tenant member read → 404 test.

## Sync/Offline Impact

Registry-row presence test.

## Acceptance Criteria

- All suites green; coverage ≥80%; `API_LOYALTY.md` matches implemented surface
  (verified by a doc-vs-OpenAPI spot check).

## Tests Required

This task IS tests + contract doc.

## Edge Cases

- OpenAPI must list every route with examples + error responses.

## Definition of Done

Evidence in PROGRESS.md; stages 19+20 marked DONE; pushed.

## Follow-up Findings

(none yet)

## Work Log

- 2026-09-10: task written.
