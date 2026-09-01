# TASK-008 — Company context and one-company DbContext factory

## Objective
Make the acting company an ambient/request-scoped context and force company database access through a one-company factory.
## Why
The database boundary must be structural, not a caller convention that can be forgotten.
## Scope
`ICompanyContext`; session/terminal/device resolution; `ICompanyDbContextFactory`; DI and architecture rule for one company per handler.
## Out of scope
Fan-out reads, provisioning, migrations, API, business-table retrofit, and group services.
## Affected files/components
Application context/factory ports; infrastructure DI and DbContext options; web request binding; architecture and integration tests.
## Dependencies
TASK-007; ADR-099, ADR-116, ADR-118; `docs/MULTI_COMPANY.md` §2.
## Relevant references
`CLAUDE.md` §7; `docs/DATA_MODEL.md` §§2b–3; existing tenant context and DbContext patterns named by inspection.
## Implementation requirements
Require an acting company for company operations; open exactly one company context per factory call; reject inactive/unknown/mismatched tenant company; preserve registry context separation; add an architecture test that catches two-company command handlers.
## Acceptance criteria
Existing company-scoped operations resolve through the factory; no command handler can resolve two company contexts; context selection cannot be supplied only as an unvalidated arbitrary parameter.
## Tests
Context resolution tests; factory connection tests; tenant/company mismatch tests; architecture negative test; transaction isolation test.
## Edge cases
Missing header and terminal binding, conflicting selectors, deactivated company, registry unavailable, nested scope reuse.
## Security considerations
Prevent horizontal company access; validate claims/device binding; do not trust client headers without authorization.
## Architectural decision impact
Load-bearing enforcement for ADR-099/116/118; architecture-guard review is mandatory.
## Definition of done
Factory, context, DI, guard and tests are green with documented review evidence.
## Status
COMPLETE
## Work log
- 2026-08-27: Planned as the structural company-scope seam.
