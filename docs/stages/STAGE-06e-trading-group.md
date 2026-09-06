# STAGE 06e — Trading group: Operator ID, company links, shared premises, cross-company users and tills

**Status:** NOT_STARTED · **Depends on:** 04b (the signed licence the Operator ID rides in), 06c (the registry), 06d (the saga coordinator and group services) · **Reference reading:** `docs/TRADING_GROUP.md` in full, `docs/MULTI_COMPANY.md` §1–§2, `docs/LICENSING.md` §2–§4, `docs/DECISIONS.md` ADR-121, ADR-122, ADR-123, ADR-124, ADR-127, ADR-099, ADR-116, `CLAUDE.md` §3 (R13), §7 rule 20, `docs/EXECUTION_STANDARD.md`

## Task index
## Second-pass architecture and task map

The existing objective, deliverables, business rules, acceptance criteria, and referenced documents in this stage remain authoritative. Use [the architecture map](../ARCHITECTURE.md) for project and boundary rules, then load only the references named by the eventual task.

**Architecture checklist:** WHAT/WHY come from this stage's Objective; affected layers/components come from its Deliverables; data, API, security, multi-company, synchronization, licensing, and testing rules come from the linked authority documents. Missing answers are **NEEDS ARCHITECTURAL CLARIFICATION**. Existing ADRs in the header apply; a new ADR is required only for a new decision. Nothing outside stated scope may change.

| ID | TYPE | TITLE | DEPENDENCIES | STATUS |
|---|---|---|---|---|
| 06e-MAP-01 | ARCHITECTURE | Stage-specific architecture decomposition and implementation task map | Stage dependencies in header | NOT_STARTED |

This is a planning gate, not an implementation task. Before this stage is selected, replace it with independently executable task files using the canonical template in docs/tasks/README.md.

| Task ID | Title | Dependencies | Status |
|---|---|---|---|
| TASK-06E-001 | Implement Operator ID and company links | Stages 04b, 06c, 06d | NOT_STARTED |
| TASK-06E-002 | Implement premises, shared bins, users, and tills | TASK-06E-001 | NOT_STARTED |
| TASK-06E-003 | Complete Stage 06e verification | TASK-06E-001, TASK-06E-002 | NOT_STARTED |

## Objective

Stages 06c and 06d made companies separate and gave them a way to cooperate. This stage decides **which
companies are allowed to cooperate at all**, and encodes the operator's rule: companies can only be
linked through an identity the vendor issues, and every cross-company operation checks that link at the
moment it happens.

It also builds the three things that fall out of linking: a **premises** that more than one company can
occupy, a **user directory** in the registry so one login can act in several companies, and the
**metering dimensions** the vendor bills on — company, named user, till.

At the end of this stage, no cross-company operation anywhere in the product can happen without an
Operator ID match, an `Active` link and the specific scope for that operation.

## What this stage does not own

- **No mixed basket.** Selling two companies' goods in one transaction is Stage **09b**. This stage
  builds the `SharedTill` scope that 09b checks; it builds no till behaviour.
- **No licence issuance.** The Operator ID is minted by the vendor control plane (Stage 30b) and signed
  into the licence by Stage 04b. This stage **reads and enforces** it and never creates one.
- **No billing.** This stage emits counters. Stage 30b prices them.
- **No shared-floor picking.** Stage 13b. This stage masters the premises bin layout and mirrors it.
- **No UI.** Endpoints only (`PROGRESS.md` §4.3).

## Deliverables

### Registry domain — `src/VumaRetail.Domain/Registry/`

| Type | File | Notes |
|---|---|---|
| `Operator` | `Operator.cs` | `OperatorId` (vendor-issued, e.g. `OP-4K2X-9QN7`), display name, licence fingerprint, `IsActive`. Never created by a tenant command — only projected from the signed licence. |
| `CompanyLink` | `CompanyLink.cs` | `CompanyAId`, `CompanyBId` (stored order-independent, smaller GUID first), `OperatorId`, `Scopes` (flags), `Status`, `AcceptedByA/B` + timestamps + licence fingerprint at acceptance, `EffectiveFrom/To`. |
| `CompanyLinkScope` | `CompanyLinkScope.cs` | `[Flags]`: `SharedFloor=1, SharedTill=2, SharedCredit=4, SharedReceipting=8, SharedSourcing=16, SharedPicking=32, SharedReporting=64`. |
| `CompanyLinkStatus` | same file | `Proposed, Accepted, Active, Suspended, Revoked`. |
| `Premises` | `Premises.cs` | Physical site: code, name, address, `GeoLocation` (ADR-113's value object), trading hours, `IsActive`. |
| `PremisesOccupancy` | `PremisesOccupancy.cs` | `PremisesId` + `CompanyId` + `StoreId` (the store row inside that company's database) + `OccupiesFrom/To`. |
| `PremisesBinLayout` | `PremisesBinLayout.cs` | The master zone/bin definitions for a premises, mirrored into each occupying company's `warehouse` schema (ADR-124). |
| `RegistryUser` | `RegistryUser.cs` | The user directory: login, contact details, `OperatorId`, `IsEnabled`. One row per human. |
| `RegistryUserCompanyAccess` | `RegistryUserCompanyAccess.cs` | `RegistryUserId` + `CompanyId` + role names in that company + `GrantedBy/At`. |
| `RegistryTerminal` | `RegistryTerminal.cs` | Terminal id, `PremisesId`, the companies it may sell for, device certificate thumbprint. |

### Application — `src/VumaRetail.Application/Registry/`

| Type | Responsibility |
|---|---|
| `ICompanyLinkService` | `RequireLink(companyA, companyB, CompanyLinkScope)` → throws `CompanyLinkRequiredException` naming both companies and the missing scope. `TryGetLink(...)`, `GetLinksFor(companyId)`. **This is the single choke point every other stage calls.** |
| `IOperatorContext` | The acting Operator ID, resolved from the licence. |
| `IPremisesService` | Occupancy, and mirroring a bin layout into an occupying company's database as a saga leg. |
| `IRegistryUserService` | Create user, grant/revoke company access, list a user's companies. |
| `IEntitlementCounters` (extend) | `CompaniesActive`, `NamedUsersPerCompany`, `TillsPerCompany`, `ActiveLinks`. |
| Commands | `ProposeCompanyLinkCommand`, `AcceptCompanyLinkCommand`, `SuspendCompanyLinkCommand`, `RevokeCompanyLinkCommand`, `CreatePremisesCommand`, `AddPremisesOccupancyCommand`, `PublishPremisesBinLayoutCommand`, `CreateRegistryUserCommand`, `GrantCompanyAccessCommand`, `RevokeCompanyAccessCommand`, `RegisterTerminalCommand`, `SetTerminalCompaniesCommand`. |

### Infrastructure

- `registry.operators`, `registry.company_links`, `registry.premises`, `registry.premises_occupancies`,
  `registry.premises_bin_layouts`, `registry.users`, `registry.user_company_access`,
  `registry.terminals`. All on the registry migration chain.
- **Unique constraint** on `(tenant_id, company_a_id, company_b_id)` with the ordering rule enforced in the
  aggregate, so one pair can hold at most one link row. Revocation is final: the row stands and the
  pair cannot be re-proposed.
- **Operator-match trigger** (ADR-121, ADR-139): a link row's `operator_id` must equal both companies'
  `operator_id`, enforced by a `BEFORE INSERT OR UPDATE` trigger — a `CHECK` cannot reference another
  table. Belt and braces behind the aggregate rule.
- Token issuance extended: the JWT carries `companies` (ids the user may act in) and `roles` per company.
  A request naming a company absent from the token is **403** (ADR-127).
- Link mutations write a durable `company-link.changed` registry outbox row and invalidate the
  `ICompanyLinkService` snapshot cache in the same transaction. A SignalR transport is deferred:
  no such transport exists anywhere in this product yet (ADR-139).

### API — `src/VumaRetail.StoreServer/Endpoints/RegistryEndpoints.cs`

```
GET    /api/v1/operator                              the acting Operator ID and its companies
GET    /api/v1/company-links                         list, filterable by company and status
POST   /api/v1/company-links                         propose
POST   /api/v1/company-links/{id}/accept             accept (the acting company, from its own context)
POST   /api/v1/company-links/{id}/suspend             suspend, with a reason
POST   /api/v1/company-links/{id}/resume              resume a suspended link
POST   /api/v1/company-links/{id}/revoke              revoke, with a reason of at least ten characters
GET    /api/v1/premises                              list
POST   /api/v1/premises                              create
POST   /api/v1/premises/{id}/occupancies             add an occupying company
POST   /api/v1/premises/{id}/bin-layout/publish      mirror the layout into occupying companies
GET    /api/v1/users                                 registry user directory
POST   /api/v1/users                                 create a registry user
POST   /api/v1/users/{id}/company-access             grant
DELETE /api/v1/users/{id}/company-access/{companyId} revoke
POST   /api/v1/terminals                             register a terminal
POST   /api/v1/terminals/{id}/companies              set the companies a till may sell for
```

Every refusal is a `ProblemDetails` with `type` = `https://vuma.dev/problems/company-link-required`
and extensions `companyA`, `companyB`, `requiredScope`.

### Permissions

`registry.grouplink.view`, `registry.grouplink.propose`, `registry.grouplink.accept`,
`registry.grouplink.revoke`, `registry.premises.manage`, `registry.user.manage`,
`registry.terminal.manage` — three-segment `module.entity.action` per ADR-013 (ADR-139). Suspending
shares the revoke permission and resuming shares the accept permission; no suspend/resume
permissions are declared.

### Entitlement and metering

- Entitlement flags: `MaxCompanies`, `MaxUsersPerCompany`, `MaxTillsPerCompany`, `MaxActiveLinks`.
- Counters are **counts only** (R10): active companies, users × companies, tills × companies, active
  links. No names, no turnover.
- Exceeding an entitlement blocks the *new* grant with a named error. It never disables what exists.

## Business rules

1. A link may only exist between two companies whose `operator_id` is identical. Enforced in the
   aggregate **and** by a database check constraint (ADR-121).
2. A link is `Active` only after both sides accept. `Proposed` grants nothing.
3. **Every cross-company operation calls `ICompanyLinkService.RequireLink` at the point of use** — not at
   configuration time, not cached across a status change (ADR-122).
4. A scope not granted means the operation is refused with the missing scope named.
5. A company whose licence has lapsed to read-only drops out of every link **for writes**; reads and
   reprints continue. The refusal names the lapse, not the link (ADR-028, ADR-123).
6. Revoking a link does not touch documents already created under it. History stands.
7. A user may be granted access only to companies under the same Operator ID.
8. A token names the companies and roles it carries; a request for another company is 403.
9. A terminal may sell for a sister company only where `SharedTill` is granted, checked per transaction.
10. A premises bin layout is mastered once and mirrored; a mirrored bin never lets a quantity span two
    companies (ADR-124).
11. Suspending a link is reversible and is the mechanism for a dispute; revoking is final and requires a
    reason string of at least 10 characters.

## Parts — the build list

Tick each as it lands. Each part builds and commits on its own.

**A. Groundwork**
- [ ] A1 — Branch `stage-06e-trading-group` off `main`
- [ ] A2 — Confirm 06c and 06d are DONE in `PROGRESS.md` §1; stop if not

**B. Domain**
- [ ] B1 — `CompanyLinkScope`, `CompanyLinkStatus`, `RegistryExceptions.cs`
- [ ] B2 — `Operator`, projected from the licence, never created by a command
- [ ] B3 — `CompanyLink` with the ordering rule, the operator-match invariant and the status machine
- [ ] B4 — `Premises`, `PremisesOccupancy`, `PremisesBinLayout`
- [ ] B5 — `RegistryUser`, `RegistryUserCompanyAccess`, `RegistryTerminal`

**C. Application**
- [ ] C1 — `ICompanyLinkService` + `CompanyLinkService` with cache and invalidation
- [ ] C2 — `IOperatorContext`, resolved from the licence claims
- [ ] C3 — Link commands (propose / accept / suspend / revoke) + validators
- [ ] C4 — Premises commands + the bin-layout mirror saga
- [ ] C5 — Registry user and terminal commands
- [ ] C6 — Entitlement counters and their registration

**D. Infrastructure**
- [ ] D1 — EF configurations, registry migration, check constraints
- [ ] D2 — JWT company/role claims; the 403 path
- [ ] D3 — `CompanyLinkChangedEvent` over SignalR, cache invalidation

**E. Wiring the choke point** — the part that makes the stage real
- [ ] E1 — `IGroupCreditService.TryHold` requires `SharedCredit`
- [ ] E2 — Group receipt allocation requires `SharedReceipting`
- [ ] E3 — Sourcing plan **and** commit require `SharedSourcing`
- [ ] E4 — Consolidated reporting requires `SharedReporting`
- [ ] E5 — Wave building requires `SharedPicking`
- [ ] E6 — Premises occupancy and putaway require `SharedFloor`
- [ ] E7 — An architecture test enumerating every cross-company entry point and asserting each one
        calls `RequireLink`. **If a new entry point is added later without a check, this test fails.**

**F. API, permissions, docs**
- [ ] F1 — `RegistryEndpoints.cs`, OpenAPI examples, ProblemDetails type
- [ ] F2 — Permissions registered in the RBAC catalogue
- [ ] F3 — Seed: one Operator ID, three companies, two links, one shared premises, two users, two tills
- [ ] F4 — `docs/DATA_MODEL.md` §4l extended; ADRs appended; `PROGRESS.md` updated

## Tests / acceptance

Name each test as written here.

- `Link_between_companies_of_different_operators_is_refused` — two companies, two Operator IDs, propose →
  refused by the aggregate; and the same insert attempted directly → refused by the check constraint.
- `Proposed_link_grants_nothing` — propose `SharedCredit`, do not accept, attempt a group credit hold →
  `CompanyLinkRequiredException` naming `SharedCredit`.
- `Accepted_link_grants_only_its_scopes` — accept with `SharedCredit` only; a credit hold succeeds, a
  sourcing commit is refused naming `SharedSourcing`.
- `Revoking_a_link_stops_new_operations_and_leaves_history` — revoke after an invoice exists; the invoice
  is unchanged and readable, a new cross-company operation is refused.
- `Lapsed_company_drops_out_of_links_for_writes` — company B read-only; a sister till selling B's stock
  is refused naming the lapse; B's own reprints still work; A trades normally.
- `Link_status_change_invalidates_the_cache_within_one_second` — suspend on node 1, node 2 refuses.
- `User_cannot_be_granted_access_across_operators`.
- `Token_without_the_company_gets_403` — not 404, and not a filtered empty result.
- `Premises_bin_layout_mirrors_into_every_occupying_company` — one publish, two companies' `warehouse`
  schemas carry identical bin codes, and a quantity in a mirrored bin belongs to exactly one company.
- `Entitlement_blocks_a_new_grant_and_never_disables_an_existing_one` — `MaxUsersPerCompany` reached; a
  new grant is refused with a named error, existing users keep working.
- `Every_cross_company_entry_point_calls_RequireLink` — the architecture test from E7, proven by
  deliberately adding an unguarded entry point in a test fixture and asserting the test fails.
- Coverage ≥ 80% on the stage's Domain + Application.

## Exit checklist

- [ ] `CLAUDE.md` §8 in full
- [ ] Every existing stage's tests green
- [ ] Registry migration reversible, `Down` **executed**
- [ ] The E7 architecture test exists and has been proven to fail on an unguarded entry point
- [ ] `multi-company-guard`, `licence-safety`, `architecture-guard` run, findings closed
- [ ] `docs/TRADING_GROUP.md` §2's "where the link is checked" table matches the code, row for row
- [ ] Seed runs and the three-company, two-link, one-premises fixture is demonstrable
