# TASK-015 — Companies API and permissions

## Objective
Expose the Stage 06c company lifecycle through the versioned API with explicit permissions and company selection.
## Why
Operators need a safe control surface for listing, provisioning, activating and deactivating companies.
## Scope
`/api/v1/companies` list/provision/activate/deactivate; `X-Vuma-Company` or terminal binding; `company.view`, `company.provision`, `company.manage`, `platform.migrate`; entitlement and error contracts.
## Out of scope
Group business APIs, cross-company money/stock operations, UI, and vendor control-plane changes.
## Affected files/components
Application commands/queries; web endpoints/auth; contracts/OpenAPI; permission catalogue; API integration tests.
## Dependencies
TASK-010, TASK-011 and TASK-012; `docs/API_STANDARDS.md`; Stage 06c; ADR-118.
## Relevant references
`docs/MULTI_COMPANY.md` §§1–2, 9; `CLAUDE.md` R7/R11; `docs/EXECUTION_STANDARD.md`.
## Implementation requirements
List active companies for ordinary operations only; protect management actions; validate header against identity/device access; enforce MultiCompany entitlement; return consistent ProblemDetails and never expose connection data.
## Acceptance criteria
Authorized users can perform the permitted operation; unauthorized/mismatched company requests are 403; non-entitled provisioning is indistinguishable from unavailable operation as specified; OpenAPI documents behavior.
## Tests
Endpoint contract and permission tests; header/binding mismatch tests; entitlement tests; lifecycle integration tests; secret-redaction test.
## Edge cases
No active companies, deactivation race, duplicate provision request, missing header, terminal-bound company override, stale token.
## Security considerations
Prevent horizontal access and privilege escalation; do not trust headers; audit lifecycle changes; avoid existence leaks where policy requires.
## Architectural decision impact
Publishes ADR-118 lifecycle and ADR-099 routing without adding a cross-company transaction.
## Definition of done
API, permissions, contracts, tests, OpenAPI and review evidence are green.
## Status
NOT_STARTED
## Work log
- 2026-08-27: Planned after lifecycle and serving guard foundations.

