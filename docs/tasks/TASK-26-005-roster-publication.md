# TASK-26-005 — Roster publication snapshot

Status: IN_PROGRESS  
Stage: 26  
Type: Domain, application, persistence, API, tests

## Objective

Publish a tenant/company-scoped roster snapshot whose canonical shift contents are represented by a
SHA-256 hash, so downstream consumers can detect stale or altered schedules.

## Scope

The workforce manage-protected publish route gathers the requested window, filters an optional store,
sorts shifts deterministically, and persists the publication metadata and hash. Shift application,
external roster distribution, labour-cost integration, and specialist review remain open.

## Verification

2026-09-13: `HrLifecycleTests` passes 11/11, including deterministic hash capture and company scope.
StoreServer and CloudApi build paths pass locally. Migration
`Stage26RosterPublication` adds the tenant/company-scoped `hr_workforce.roster_publications` table.
2026-09-14: Store-filtered publications now hash and count only the selected shifts rather than the
entire requested window. `HrLifecycleTests` passes 12/12; external distribution, labour-cost
integration, and specialist review remain open.
