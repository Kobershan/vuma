# Task TASK-002 — Complete OpenAPI examples for reopened module endpoints

## Objective

Add the missing request, response, and error examples required by the API standard to endpoints in
the reopened stages.

## Why

`PROGRESS.md` §4.22 identifies incomplete OpenAPI examples as the largest remaining mechanical
definition-of-done gap.

## Scope

Only endpoint metadata and the directly required API tests for reopened stages 09–13.

## Out of scope

Domain behavior changes, endpoint redesign, unrelated documentation or test cleanup.

## Files/components likely affected

`src/VumaRetail.Web/` endpoint files and relevant API integration tests.

## Dependencies

TASK-001 should be complete first. The API contract in `docs/API_STANDARDS.md` is authoritative.

## Relevant references

`docs/API_STANDARDS.md`, relevant stage documents, `docs/PROGRESS.md` §4.22.

## Implementation requirements

Every affected operation must expose useful summaries, request/response examples, and required
error responses without changing runtime behavior.

## Acceptance criteria

- Missing examples are identified mechanically and closed.
- `/openapi/v1.json` contains the expected examples.
- API tests pass and no operation loses existing auth or error behavior.

## Tests required

Relevant API integration tests and OpenAPI document validation.

## Edge cases

Malformed requests, unauthorized requests, forbidden requests, validation failures, conflicts, and
module-specific errors must remain represented.

## Security considerations

Examples must contain synthetic data only; never credentials, tokens, customer data, or secrets.

## Architectural decision impact

None expected.

## Definition of done

All affected operations are documented, validated, tested, reviewed, and recorded in the task and
`CURRENT.md`.

## Status

NOT_STARTED
