# API STANDARDS — Vuma Retail

> The contract every module stage writes against. ADR-008: no capability exists in a UI before it
> exists in the API, so this document is read before an endpoint is written, not after.

Built in Stage 03. Where this document and the code disagree, the code is the bug — except where it
says **specified, built later**, which marks a decision made now so nobody has to make it twice.

---

## 1. Hosts

| Host | Audience | DTOs | Auth |
|---|---|---|---|
| `VumaRetail.StoreServer` | staff, terminals, the desktop shell, the Android app | `VumaRetail.Contracts` | JWT, terminal certificate |
| `VumaRetail.CloudApi` | the same, plus store→cloud sync | `VumaRetail.Contracts` | JWT, mTLS |
| `VumaRetail.PublicApi` | storefronts, loyalty partners, the open internet | **its own** (ADR-021) | API keys, member tokens |

The public host does not share DTOs with the other two and never will. `CLAUDE.md` §7 rule 14 makes
cost, margin and supplier data structurally unreachable there rather than filtered, and an
architecture test fails the build on a reference in that direction.

## 2. Versioning

URL segment: `/api/v1/...`. One major version per breaking change, and a breaking change is a
decision with a migration plan, not a chore.

- `VumaApi.MapVumaApi()` returns the group for the current version. Every endpoint hangs off it.
- Every response carries `X-Vuma-Api-Version: v1`.
- **Un-versioned routes are a closed list**: `/health` and `/openapi/*`. They are infrastructure — a
  load balancer should never be reconfigured because the business API moved to v2. The list lives in
  `VumaApi.UnversionedRoutes` and an API test walks the live endpoint table to enforce it.

**Additive changes do not bump the version.** A new endpoint, a new optional request field, a new
response field. Clients must ignore fields they do not know; the Android app and the desktop shell
both do.

**Breaking changes do**: removing or renaming a field, narrowing a type, changing a status code for
an unchanged condition, making an optional request field required, or changing the meaning of an
error code. Adding a *new* error code is additive — a client that does not know it falls through to
its status code, which is why the status code always has to be right on its own.

## 3. Shapes

- Resources are plural nouns: `/api/v1/items`, `/api/v1/purchase-orders`. Kebab-case in paths,
  `camelCase` in JSON.
- A verb in a path is allowed only for an action that is not a resource state change:
  `/api/v1/auth/token`, `/api/v1/stocktakes/{id}/post`. Prefer the resource.
- `GET` never changes state. `POST` creates or acts. `PUT` replaces. `PATCH` merges. `DELETE`
  soft-deletes (`CLAUDE.md` §7 rule 8 — nothing is hard-deleted, so a `DELETE` sets `deleted_at`).
- IDs in paths are UUID v7 (ADR-004).
- All timestamps are ISO-8601 with an offset, in UTC (`CLAUDE.md` §7 rule 9).
- Money is always an amount **and** a currency code. Never a bare number (`CLAUDE.md` §7 rule 4).
- Quantities are always a number and a unit of measure.

## 4. Status codes

| Code | When |
|---|---|
| `200` | read, or an action that returns something |
| `201` | created, with `Location` |
| `202` | accepted for later work — an import batch, a planning run |
| `204` | done, nothing to say |
| `400` | the request could not be used. Two distinct causes, distinguished by `code`: `VALIDATION_FAILED` when it bound and a validator rejected a value (`errors` names the properties), `MALFORMED_REQUEST` when binding itself failed and there is nothing to enumerate |
| `401` | no usable credential |
| `403` | authenticated, but the permission is missing (ADR-013) |
| `404` | not there — **or not this tenant's**, which is answered identically on purpose |
| `409` | something that must be unique already is not |
| `422` | well formed, and a business rule said no |
| `429` | rate limited (*specified, built later* — Stage 20/21) |
| `500` | server fault; the body carries a correlation id and nothing else |
| `503` | temporarily unavailable. On the public API this is also the **neutral** answer to a write during a licence lapse — a tenant's own customers are never told their billing status (ADR-028) |

`404` for another tenant's row is not a nicety. Tenant isolation is a global query filter, so the row
genuinely is not there; distinguishing "not found" from "not yours" would turn every id-taking
endpoint into an existence oracle across tenants.

## 5. Errors

Every error is an RFC 7807 problem document, `application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.21",
  "title": "Conflict",
  "status": 409,
  "detail": "Another active operator already uses that PIN. Choose a different one.",
  "instance": "/api/v1/users/019.../pin",
  "code": "IDENTITY_PIN_TAKEN",
  "correlationId": "0192a1b2c3d47e8f9a0b1c2d3e4f5a6b"
}
```

- **`code` is the contract.** `SCREAMING_SNAKE_CASE`, stable across releases. Clients branch on it
  and never on `detail`, which is reworded whenever it reads badly.
- **`detail` is for a human** and may name values, but never another tenant's data, never a stack
  trace, and never which of several credentials was wrong.
- **`correlationId`** is on every error and echoed in `X-Correlation-Id`. It is the whole payload of
  a `500`, and it is what support asks for.
- A `VALIDATION_FAILED` `400` adds `errors`: `{ "certificateThumbprint": ["A certificate thumbprint is 64 hexadecimal characters (SHA-256)."] }`.
- A `MALFORMED_REQUEST` `400` does not, and cannot: the request never bound, so there is no property
  to name. Its `detail` carries the framework's own reason — *"Required parameter \"DateOnly asOf\"
  was not provided from query string"* — and nothing from inside the body, because a JSON parse
  failure's inner exception carries a fragment of the caller's payload and a path into it.

**A request that never reaches a handler is still the caller's to fix.** Minimal APIs raise
`BadHttpRequestException` when a required query or route parameter is missing or unparseable, when a
body cannot be deserialised, or when a body is too large. That is mapped in `VumaExceptionHandler`
alongside the domain kinds, honouring the exception's own status so an oversized body stays a `413`
rather than being flattened to `400`. It went unmapped until Stage 07, and the consequence was worth
recording: every such request returned `500 INTERNAL_ERROR`, telling a caller the server had broken
when their request was merely incomplete, and logging an error each time — so one client's bad
integration read as an outage.

Platform codes live in `VumaRetail.Contracts.ApiErrorCodes`. Module codes live with the exception
that raises them — a central list of four hundred codes is a file nobody keeps accurate.

**Choosing a status from the domain.** A `DomainException` carries a `DomainProblemKind`:
`Rule → 422`, `NotFound → 404`, `Conflict → 409`, `Malformed → 400`. That is the domain's own
vocabulary, not HTTP leaking downwards, and the mapping exists in exactly one place
(`VumaExceptionHandler`).

**Sign-in is one answer.** Every authentication failure — no such user, wrong password, wrong PIN,
locked out, deactivated — is `401` with `AUTH_INVALID_CREDENTIALS` and the same `detail`. There is a
test that compares the two bodies.

## 6. Authentication and authorisation

- `Authorization: Bearer <jwt>`, 15-minute access tokens, 30-day rotating refresh tokens.
- Terminals authenticate with a pinned client certificate; a POS PIN is only ever accepted on a
  session that is already terminal-authenticated (ADR-041).
- **Authorise by permission, never by role name.** `.RequirePermission("inventory.stocktake.approve")`
  with a constant from the owning module's `IModulePermissions`. There is an architecture test.
- Permissions are resolved per request, not read from the token: a permission revoked at 09:00 must
  not still work at 09:14.

## 7. The command pipeline

An endpoint does three things: bind, dispatch, map the result to a status code. It does not open a
transaction, validate, or call a handler directly.

```csharp
group.MapPost("/stocktakes", async (CreateStocktakeRequest request, IDispatcher dispatcher, CancellationToken ct) =>
    TypedResults.Ok(await dispatcher.SendAsync(new CreateStocktakeCommand(request.StoreId), ct)))
    .RequirePermission(InventoryPermissions.StocktakeCreate);
```

The pipeline runs `Logging(0) → ReadOnlyGuard(50) → Validation(100) → Transaction(200) → Outbox(300) → handler`.
Slots 50 and 300 are reserved for Stages 04b and 04 and are empty today; the order is asserted by
tests so neither stage has to renumber anything.

**Handlers do not commit.** The pipeline opens the transaction and commits once (ADR-044). A handler
that takes `IUnitOfWork` fails the architecture tests.

## 8. Collections and pagination

*Specified now, built by Stage 06 — the first stage with a collection worth paging.*

Keyset pagination, not offset: a till scrolling a 250 000-item catalogue while stock is moving must
not see an item twice or miss one, which is exactly what `OFFSET` does under concurrent writes.

```
GET /api/v1/items?limit=50&after=019263...
{ "items": [ … ], "nextCursor": "019264…", "hasMore": true }
```

`limit` defaults to 50 and is capped at 200. Sorting is `?sort=code` / `?sort=-code`, restricted to
an allow-list per endpoint — an arbitrary sort column is an unindexed sequential scan on somebody's
busiest table.

## 9. Idempotency

*Specified now, enforced by Stage 04's sync receiver and the Stage 20/21 public hosts.*

Any `POST` that creates something a client might retry accepts `Idempotency-Key`. The same key with
the same body returns the original response; the same key with a different body is `409`. This is the
same guarantee ADR-006's inbox gives sync, expressed at the HTTP edge — R1 means terminals retry, and
a retried sale must not become two sales.

## 10. OpenAPI

`/openapi/v1.json`, generated by the document generator built into ASP.NET Core 9. Every endpoint
appears (`CLAUDE.md` §8), with:

- a `summary` written for somebody who has never seen the codebase,
- request examples,
- the seven standard error responses, attached by an operation transformer rather than by hand —
  which is what makes it true of the fortieth endpoint as well as the first.

An API test asserts the document contains every route and that each operation carries the error
responses.

## 11. Logging and correlation

- `X-Correlation-Id` in, echoed out, minted when absent, on every log line for the request.
- One structured line per message through the pipeline, with the outcome and the duration.
- **The message payload is never logged** — a `CreateUserCommand` holds a password. On top of that,
  the log sink redacts any property whose name contains `password`, `pin`, `token`, `secret` or
  `certificate`, at any depth (`docs/SECURITY.md` §4).

## 12. What a module stage owes the API

- [ ] Every capability reachable over the versioned API before any UI exists for it (ADR-008)
- [ ] Permissions declared in an `IModulePermissions` and required on every endpoint
- [ ] Commands classified with `[CommandSideEffect]` (ADR-034); handlers that do not commit
- [ ] Validators for every command; the handler keeps its own guard as well
- [ ] Domain exceptions carrying a stable code and the right `DomainProblemKind`
- [ ] `summary` and request examples so the endpoint is usable from the OpenAPI document alone
- [ ] API-level tests: happy path, `401`, `403`, `422` and the module's own conflict cases
      (`docs/TESTING.md` §1)
