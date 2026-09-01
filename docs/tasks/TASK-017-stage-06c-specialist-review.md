# TASK-017 — Architecture, multi-company and sync specialist review

## Objective
Run and close the Stage 06c specialist panel against the completed implementation.
## Why
Database boundaries, saga safety and sync isolation are cross-cutting properties easy for an implementation session to miss.
## Scope
`architecture-guard`, `multi-company-guard`, and `sync-and-offline` reviews; worked examples for findings; fixes limited to Stage 06c.
## Out of scope
Stage 06d group feature review, unrelated historical defects, and broad refactoring.
## Affected files/components
All Stage 06c changed files and tests; review reports; task/stage evidence.
## Dependencies
TASK-016.
## Relevant references
`docs/AGENTS.md`; Stage 06c Exit checklist; `docs/MULTI_COMPANY.md` §11; ADR-099, ADR-116–ADR-120.
## Implementation requirements
Run all three reviewers; require concrete worked examples; fix findings before proceeding; record environment-unverified checks honestly and do not treat them as PASS.
## Acceptance criteria
Each applicable reviewer reports PASS or a documented, accepted limitation with follow-up; no open critical/high boundary or sync finding remains.
## Tests
Reviewer-specified regression tests; rerun relevant suite after every fix; architecture suite and migration/restore tests.
## Edge cases
Reviewer unavailable, Docker/WPF unavailable, finding conflicts with locked ADR, source changed outside task scope.
## Security considerations
Review logs and fixtures contain no secrets; reviewers must test horizontal isolation and credential redaction.
## Architectural decision impact
Validates, but does not silently amend, ADR-099 and ADR-116–120; proposed changes require a new ADR.
## Definition of done
Panel evidence, closed findings, rerun results and limitations are recorded.
## Status
NOT_STARTED
## Work log
- 2026-08-27: Planned as the mandatory pre-closure review session.

