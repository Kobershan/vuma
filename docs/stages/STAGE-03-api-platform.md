# STAGE 03 — API platform: versioning, ProblemDetails, OpenAPI, the CQRS pipeline

**Status:** DONE (2026-08-10) · **Depends on:** 02 · **Reference reading:** `CLAUDE.md` §3 (R3), §4 (CQRS, validation, logging rows), §7 (rules 2, 5, 10, 11, 15), §8, `docs/CONVENTIONS.md` §3–§5, `docs/DECISIONS.md` ADR-008, ADR-009, ADR-028, ADR-034, ADR-039, ADR-040, `docs/TESTING.md` §1–§2, `docs/SECURITY.md` §4, `docs/API_STANDARDS.md` (written by this stage)

## Task index
## Second-pass architecture and task map

The existing objective, deliverables, business rules, acceptance criteria, and referenced documents in this stage remain authoritative. Use [the architecture map](../ARCHITECTURE.md) for project and boundary rules, then load only the references named by the eventual task.

**Architecture checklist:** WHAT/WHY come from this stage's Objective; affected layers/components come from its Deliverables; data, API, security, multi-company, synchronization, licensing, and testing rules come from the linked authority documents. Missing answers are **NEEDS ARCHITECTURAL CLARIFICATION**. Existing ADRs in the header apply; a new ADR is required only for a new decision. Nothing outside stated scope may change.

| ID | TYPE | TITLE | DEPENDENCIES | STATUS |
|---|---|---|---|---|
| 03-MAP-01 | ARCHITECTURE | Stage-specific architecture decomposition and implementation task map | Stage dependencies in header | NOT_STARTED |

This is a planning gate, not an implementation task. Before this stage is selected, replace it with independently executable task files using the canonical template in docs/tasks/README.md.

| Task ID | Title | Dependencies | Status |
|---|---|---|---|
| TASK-03-001 | Complete API platform | Stage 02 | COMPLETE |

## Objective

Stage 02 left eight command handlers each committing for itself, endpoints returning bare `401`
and `200`, no dispatcher, no error contract and no OpenAPI document. Every module stage from 06
onward writes commands, queries and endpoints. This stage builds the machinery they all plug into,
so that forty later modules configure rather than invent.

Three things become true here and stay true for the rest of the build:

1. **A command's transaction is the pipeline's, not the handler's.** `IUnitOfWork`'s own XML doc has
   said so since Stage 01; until now no pipeline existed to make it true. It becomes true today, and
   an architecture test keeps it that way.
2. **Every error a client sees is a `ProblemDetails` with a stable machine-readable code**
   (`CONVENTIONS.md` §5). The code is the contract; the message is for humans.
3. **The pipeline has named, numbered slots.** Stage 04's outbox and Stage 04b's read-only
   interceptor are single registrations into slots that already exist, rather than surgery on a
   dispatcher forty modules depend on.

## Deliverables

**Application — pipeline abstractions**
- `IPipelineBehaviour` — one behaviour, wrapping the next. Non-generic over the message so the
  dispatcher composes a chain without a closed generic per behaviour per message.
- `MessageEnvelope` — what a behaviour is told about the message in flight: its type, its name,
  whether it is a command or a query, and its `SideEffect` / `ReadOnlyExemption` classification read
  from `[CommandSideEffect]`. Stage 04b's interceptor needs exactly this and nothing more.
- `PipelineOrder` — the numbered slots, outermost to innermost: `Logging(0)`,
  `ReadOnlyGuard(50, Stage 04b)`, `Validation(100)`, `Transaction(200)`, `Outbox(300, Stage 04)`.
  Reserving the numbers now is what stops a later stage renumbering the chain.
- `ValidationFailedException` — a `DomainException` carrying per-property failures, code
  `VALIDATION_FAILED`.
- `ICorrelationContext` — the id that ties a log line, an audit row and a `ProblemDetails` body to
  one request.

**Infrastructure — the pipeline**
- `Dispatcher` (ADR-009) — resolves the handler for a runtime message type through a cached
  generic executor rather than `MethodInfo.Invoke`, and runs it through the ordered behaviours.
- `LoggingBehaviour` — one structured log line per message with outcome and duration, correlated.
- `ValidationBehaviour` — runs the FluentValidation validators Stage 02 already wrote.
- `TransactionBehaviour` — commands only. Opens the transaction, commits once after the handler
  returns. Queries never open one.
- `CorrelationContext`, and `AddVumaMessaging()` with the Scrutor scan ADR-009 names — replacing the
  eight hand-written handler registrations Stage 02 left behind.

**`VumaRetail.Web` — the API edge**
- `VumaProblemDetails` + `VumaExceptionHandler` — the whole exception surface mapped once:
  `ValidationFailedException` → `400`, `DomainException` → `422`, `IdentityNotFoundException` →
  `404`, anything else → a neutral `500` that leaks nothing but the correlation id.
- `CorrelationIdMiddleware` — reads or mints `X-Correlation-Id`, echoes it, pushes it into the log
  context.
- `ApiVersioning` — `MapVumaApi(version)`, URL-segment versioning, `X-Vuma-Api-Version` on every
  response. Hand-rolled for the same reason the dispatcher is (ADR-043).
- `VumaOpenApi` — the built-in .NET 9 document at `/openapi/v1.json`, with the bearer security
  scheme, request/response examples, and the standard error responses attached to every operation
  by a transformer rather than by forty `Produces` calls.
- `VumaLogging` — Serilog to console + rolling file, with `password`, `pin`, `token`, `secret` and
  `certificate` redacted anywhere they appear in a log event (`docs/SECURITY.md` §4).
- The Stage 02 endpoints re-hung on the versioned group, with their error bodies and OpenAPI
  metadata filled in, and a neutral `AUTH_INVALID_CREDENTIALS` for every sign-in failure.

**Docs**
- `docs/API_STANDARDS.md` — versioning, resource shapes, status codes, the error catalogue,
  pagination, idempotency, and what a module stage owes the API.

## Business rules

- **The pipeline owns the transaction.** A handler mutates tracked entities and returns; it does not
  take an `IUnitOfWork` and does not call `CommitAsync`. Two handlers that each commit make a
  half-applied command possible, and ADR-006's outbox row must land in the same transaction as the
  change that produced it — which is unachievable if handlers commit whenever they like. An
  architecture test fails the build on a handler that depends on `IUnitOfWork`.
- **`AuthenticationService` still commits for itself**, and that is the one exemption. It is not a
  command and never enters the pipeline (ADR-040).
- **A query never opens a transaction.** Reporting under read-only must stay cheap, and a read that
  takes a write transaction is the thing that turns a slow report into a store-wide lock.
- **Validation runs before the handler, never inside it.** The handler keeps its own guard as well —
  the validator is the contract at the edge, the guard is what makes the domain rule true no matter
  who calls it. Both are tested.
- **Every error carries a stable code.** Reworded messages must never break an integration, so
  clients branch on `code` and nothing else.
- **Sign-in failure is one answer.** `ProblemDetails` must not reintroduce the distinction between
  "no such user", "wrong password" and "locked out" that Stage 02 deliberately collapsed.
- **A 500 says nothing.** No stack trace, no exception type, no SQL. The correlation id is the whole
  payload, and it is what support asks for.
- **Nothing sensitive reaches a log.** Redaction is by property name across the whole log event, not
  a discipline applied at each call site.
- **Every route is versioned.** `/api/v1/...`. Un-versioned infrastructure routes are a closed,
  named list — `/health`, `/openapi/*` — asserted at runtime against the endpoint table.

## Tests / acceptance

- Dispatcher: resolves a handler by runtime message type; a message with no handler fails with a
  message naming the type; behaviours run in `PipelineOrder` sequence, outermost first
- Validation: an invalid command is refused before the handler runs; a valid one reaches it; the
  failures are grouped per property
- Transaction: a command commits once, through the pipeline, with no `CommitAsync` in the handler;
  a query opens no transaction; a handler that throws leaves nothing written
- Redaction: `password`, `pin`, `token`, `secret` and `certificate` are masked in a log event, in
  nested structures and in the message template arguments alike
- ProblemDetails: `422` for a domain rule with its code, `400` with per-property errors for a
  validation failure, `404` for a missing entity, `401` unauthenticated, `403` without the
  permission, `500` carrying nothing but a correlation id
- Correlation: a supplied `X-Correlation-Id` is echoed; one is minted when absent; it appears in the
  error body
- OpenAPI: `/openapi/v1.json` is served, contains every endpoint, and every operation carries the
  standard error responses
- Versioning: every mapped route is under `/api/v{n}` except the named infrastructure routes, and
  `X-Vuma-Api-Version` comes back on an API response
- API level (`WebApplicationFactory` over real PostgreSQL, `docs/TESTING.md` §1): sign in, call a
  permission-gated endpoint with and without the permission, and get the four error shapes
- Architecture: a command handler that takes `IUnitOfWork` fails the build. Proven by a deliberate
  violation.

## Exit checklist

- [x] `dotnet build -c Release` — **0 warnings, 0 errors** solution-wide
- [x] `scripts/test.sh` — **295 passed, 0 failed** (130 unit, 22 architecture, 143 integration),
      identical across three consecutive runs
- [x] Domain + Application line coverage **95.0%** (889 of 936 lines) — the §8 bar is 80%
- [x] No EF migration required — this stage adds no entity. `has-pending-model-changes` reports the
      model and migrations agree, and up → down → up was run locally exactly as CI's `migrate-check`
      does it
- [x] `/openapi/v1.json` served, containing all seven endpoints, each operation carrying the seven
      standard error responses, and the sign-in operation carrying a request example — asserted by
      three API tests against the live document
- [x] Every endpoint goes through the dispatcher; no handler takes `IUnitOfWork` and no handler calls
      `CommitAsync`
- [x] Two new architecture rules, **proven to fail on a deliberate violation** (see below)
- [x] `docs/API_STANDARDS.md` written
- [x] `scripts/seed.sh` still builds the demo tenant through the new pipeline — 2 stores, 3 roles,
      3 users, 22 grants, 1 enrolled terminal — and a second run creates nothing further
- [x] `docs/PROGRESS.md` updated, ADR-043 to ADR-045 appended, committed and pushed

### Enforcement proven, not assumed

`CreateRoleCommandHandler` was given back its `IUnitOfWork` and its `CommitAsync` call, and both new
rules fired before the change was reverted:

| Rule | Reported as |
|---|---|
| No message handler takes the unit of work | `VumaRetail.Application.Identity.Commands.CreateRoleCommandHandler` |
| No handler calls `CommitAsync` | `src/VumaRetail.Application/Identity/Commands/RoleCommands.cs:63 — await unitOfWork.CommitAsync(cancellationToken)…` |

The first attempt did not even reach the tests: the XML documentation rule failed the Application
build on the undocumented constructor parameter, which is `CLAUDE.md` §7 rule 10 doing its job one
layer earlier than expected.

### Two live bugs the stage's own tests found

Both were latent in Stage 02 and neither was reachable by a service-level test.

- **`MapInboundClaims` rewrote `sub`.** Every `FindFirstValue("sub")` returned null, so
  `GET /api/v1/me/permissions` answered `401` on a valid token and every permission-gated endpoint
  answered `403` for a user who held the permission. Fixed in ADR-045. This is the argument for
  `docs/TESTING.md` §1's API level, made concrete on the first run.
- **`DemoSeed` resolved command handlers directly.** With the commit moved to the pipeline it would
  have run to completion, printed an activation code, and persisted nothing. Found by the new
  `CommitAsync` architecture rule, not by a test of the seed. It now uses `IDispatcher`.

### Deviations from the plan, and why

- **The dispatcher and the behaviours live in `Infrastructure`, not `Application`.** Only the
  abstractions — `IDispatcher`, `IPipelineBehaviour`, `MessageEnvelope`, `PipelineOrder`,
  `ValidationFailedException`, `ICorrelationContext` — are in `Application`. Resolving
  `IValidator<TCommand>` for a runtime type is a container concern, and putting it in `Application`
  would have meant a DI dependency there for no gain. Application defines ports; Infrastructure
  implements them, exactly as everywhere else.
- **`DomainException` gained a `DomainProblemKind`.** Mapping a domain failure to a status code
  needed *some* signal, and the alternatives were worse: a per-code table in `Web` that grows with
  every module, or a status code on the exception, which is HTTP leaking into the domain.
  `Rule | NotFound | Conflict | Malformed` is the domain's own vocabulary and maps in one place.
- **API tests live in `VumaRetail.IntegrationTests` rather than a new project.** They need the same
  PostgreSQL fixture, and a second project would either duplicate it or reference the first. The
  project now also references `VumaRetail.StoreServer` so the tests run the real host.
- **`PostgresFixture` turns pooling off** for the per-test databases. Every test gets its own
  database and so its own connection pool, and a pool keeps connections open long after its test has
  finished; at around a hundred tests that hits PostgreSQL's default `max_connections` and the suite
  starts failing in whichever test happens to run hundredth. Adding this stage's tests is what
  reached the limit.

## Explicitly deferred

- **OpenTelemetry OTLP export.** `CLAUDE.md` §4 wants Serilog → rolling file **and** OTLP → cloud.
  The cloud sink has nothing to talk to until Stage 04 exists and no tenant to attribute a trace to
  until Stage 04b does. The file and console sinks ship here; the OTLP sink is a configuration line
  added by Stage 04.
- **The outbox behaviour.** Slot 300 is reserved and empty. ADR-006 is Stage 04's.
- **The read-only behaviour.** Slot 50 is reserved and empty. ADR-028 is Stage 04b's.
- **Rate limiting and idempotency keys.** `API_STANDARDS.md` specifies both because the contract has
  to be stated before anyone writes against it; enforcement belongs with the public API host
  (Stage 20/21) and the sync receiver (Stage 04), which are the two surfaces that need it.
- **Pagination helpers.** Specified in `API_STANDARDS.md`, built by Stage 06 — the first stage with
  a collection big enough to page.
