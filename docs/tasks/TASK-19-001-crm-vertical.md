# Task

## Status

COMPLETE

## Stage

Stage 19 — CRM: 360° customer view, leads, opportunities, activities, segments, consent

## Type

DOMAIN, APPLICATION, INFRASTRUCTURE, DATABASE, API, TESTING

## Objective

Build the full Stage 19 vertical: `crm` schema with six tables, command/query handlers,
internal service ports for Stage 20/22 (`IConsentService`, `ISegmentService`,
`ICustomer360ViewService`), staff REST endpoints, permissions, entitlement manifest, migration,
unit + integration tests, seed.

## Why

Stage 20 (loyalty) cannot gate on consent/segments without Stage 19's contracts, and Stage 22
needs segment evaluation + consent state. The stage doc exists (`docs/stages/STAGE-19-crm.md`,
NOT_STARTED); only Domain scaffolding + 44 unit tests exist (commit `b6b83be`).

## Scope

- Domain (`src/VumaRetail.Domain/Crm/`, one type per file per CONVENTIONS.md §1):
  - `Lead` (Entity, `[Replicated(StoreToCloud, CloudWins)]`): names, email/phone/company,
    `LeadSource`, `LeadStatus`, `AssignedTo`, `CustomerId` (nullable link, NO cross-schema FK),
    `ConvertedAt`. `Convert(customerId, at)` — one-way; `LeadAlreadyConvertedException`.
  - `Opportunity` (Entity, same replication): title/description, `Money ExpectedValue`,
    `OpportunityStage`, `Probability` 0–100, `CloseDate`, `LossReason`, `LeadId`/`CustomerId`
    (nullable Guids, no FKs). Won requires CustomerId; Lost requires LossReason.
  - `Activity` (Entity, `IImmutableRecord`, `[Replicated(StoreToCloud, AppendOnly)]`):
    type/direction/subject/body/happenedAt/duration/parent refs/customerId. No mutators
    except base audit stamps (remove scaffolding's `new` shadowing).
  - `Segment` (Entity, CloudWins): name/description/kind/query-expression/is-active.
    Static/dynamic refusal rules stay.
  - `SegmentMember` (Entity, CloudWins): SegmentId, MemberType, MemberId, AddedAt/AddedBy.
    Composite natural uniqueness (segment, type, member).
  - `Consent` (MOVED from `VumaRetail.Domain.Loyalty` — Stage-19 ownership): per
    (ConsentType, CustomerId) uniqueness, Given/Withdrawn/Expired/NotAsked, Grant/Withdraw/
    Expire transitions with explicit timestamps, `IsValid(at)`.
  - `CrmEnums.cs`, `CrmExceptions.cs` (stable codes: LEAD_ALREADY_CONVERTED,
    OPPORTUNITY_MISSING_CUSTOMER, OPPORTUNITY_LOSS_REASON_REQUIRED, SEGMENT_*,
    CONSENT_*, ACTIVITY_IMMUTABLE).
  - All timestamps explicit parameters (wall-clock arch test); `CompanyId` via
    `AssignCompany` at creation (base mapping requires it).
- Application (`src/VumaRetail.Application/Crm/`):
  - Ports `CrmPorts.cs`: `ILeadRepository`, `IOpportunityRepository`, `IActivityRepository`,
    `ISegmentRepository`, `ISegmentMemberRepository`, `IConsentRepository` + internal
    `IConsentService` (`GetStateAsync`, `IsValidAsync`), `ISegmentService` (`IsMemberAsync`),
    `ICustomer360ViewService` (`GetViewAsync`).
  - Commands (Write): CreateLead, UpdateLead, ConvertLead (link-only v1: requires existing
    customer Guid, verified through Partners read port; auto-create deferred — no
    create-customer port exists), CreateOpportunity, UpdateOpportunityStage, WinOpportunity,
    LoseOpportunity, LogActivity, CreateSegment, AddStaticMember, GiveConsent,
    WithdrawConsent. Validators for each. Queries: Get/List leads & opportunities,
    GetActivities, GetSegmentMembers, GetConsentState, GetCustomer360View.
  - `CrmPermissions` (`crm.lead.view/manage`, `crm.opportunity.view/manage`,
    `crm.activity.log/view`, `crm.segment.view/manage`, `crm.consent.manage/view`,
    `crm.view360`), `CrmModuleManifest` (flag `crm`).
- Infrastructure: `Schemas.Crm`, EF configurations (snake_case, unique indexes incl.
  `ux_crm_consents_customer_type`, `ux_crm_leads_email_store`), repositories, `AddVumaCrm()`,
  DbSets.
- API: `src/VumaRetail.Contracts/Crm/CrmContracts.cs` + `src/VumaRetail.Web/Crm/
  CrmEndpoints.cs` (`/api/v1/crm/...`, `RequireModule("crm")`, per-endpoint permissions,
  summaries).
- Database: one reversible migration (`Stage19_Crm`); Down tested.

## Out of Scope

Customer identity creation (Stage 06 Partners owns it); loyalty (Stage 20); campaigns
(Stage 22); 360° write-side materialisation (read projection only).

## Architecture

Single-company handlers (scoped repositories, never `VumaRetailDbContext` directly, never
`IUnitOfWork`, never two company resolvers). No cross-schema FKs (Guid refs only). No GL
refs. Every command `[CommandSideEffect]`. Handlers take `IClock`, never wall clock.

## Dependencies

Stage 06 (Partner read for link check), 04b (entitlement/read-only via pipeline), 01 (base
entity/audit).

## Relevant Files

`docs/stages/STAGE-19-crm.md`, `src/VumaRetail.Domain/Crm/`, `src/VumaRetail.Application/
Planning/Commands/ForecastCommands.cs` (pattern), `src/VumaRetail.Web/Planning/
PlanningEndpoints.cs` (pattern), `src/VumaRetail.Infrastructure/Persistence/
Configurations/Planning/PlanningConfigurations.cs` (pattern).

## Relevant Documentation

`docs/stages/STAGE-19-crm.md`, CONVENTIONS.md §1–§6, TESTING.md §1–§2, API_STANDARDS.md §12,
DATA_MODEL.md §1–§3, SECURITY.md §1, LICENSING.md §1–§4, ADR-150, ADR-151.

## Implementation Requirements

- Replace `src/VumaRetail.Domain/Crm/Crm.cs` + move `Consent` out of
  `src/VumaRetail.Domain/Loyalty/Loyalty.cs`; update affected scaffolding unit-test call
  sites with explicit timestamps/company (explicit supersession per ADR-150; intent kept).
- Idempotency: `ConvertLead` replays safe (Converted check first); `LogActivity` accepts
  client idempotency key? v1: natural-key dedupe on (type, subject, happenedAt, customer)
  NOT applied — activities are append-only facts; duplicates are caller error. Document.
- POPIA: consent audit via platform audit trail; anonymisation hook documented, not built.

## Data/Database Impact

New schema `crm`, six tables, all base columns + `company_id` required. No changes to
existing tables.

## API Impact

New group `/api/v1/crm/*` on StoreServer; OpenAPI via existing generator. No breaking
changes.

## Security

Permission per endpoint; tenant isolation via global filter; consent/activity reads scoped.

## Multi-Company/Tenant Impact

`company_id` required everywhere; one DB per company (no cross-DB work); customer link is a
Guid, resolved in the owning company's DB.

## Sync/Offline Impact

All six entities `[Replicated]`; rows added to `docs/SYNC_AND_BACKUP.md` §3 registry.
Activity logging tolerates offline (queued client-side; server replays idempotently by
client-supplied id).

## Acceptance Criteria

- All stage-doc business rules 1–12 enforced + tested.
- `IConsentService.IsValidAsync` + `ISegmentService.IsMemberAsync` + 360 view query work
  from Stage 20's perspective (consumer-driven test).
- Migration Up/Down round-trips on scratch PG.

## Tests Required

- Unit: updated 17 Crm scaffolding tests (green) + Opportunity state machine, consent
  expiry matrix, segment static/dynamic matrix, 360 projection pure test.
- Integration (real PG): every command + query handler; convert-link flow incl. converted
  activity row; consent withdraw visibility in 360 view; permission 403/404 paths at API
  level where harness allows.
- Coverage ≥80% on new Domain+Application.

## Edge Cases

- Convert with unknown customer Guid → 404/422, lead untouched.
- Win without CustomerId → 422 OPPORTUNITY_MISSING_CUSTOMER; Lose without reason → 422.
- Dynamic segment AddMember → 422; re-consent after withdraw creates new state (v1: new
  row per (type,customer) is unique — re-give after withdraw flips to Given with fresh
  GrantedAt; audit preserves history).
- Duplicate (email, store) lead → 409.

## Definition of Done

CLAUDE.md §8: build 0 warnings (Domain/Application), tests green, migration reversible,
OpenAPI entries, permissions + manifest + metering-by-schema, seed row, docs updated,
committed + pushed.

## Follow-up Findings

- Lead auto-create on convert (needs a create-customer port from Stage 06 Partners).
- Consent/Activity anonymisation job (retention sweeper).
- Dynamic-segment query language (v1 stores expression, evaluates equality predicates only).

## Work Log

- 2026-09-10: CRM domain, application, infrastructure, migration, API, permissions, manifest, seed, and tests verified on main.
- 2026-09-10: Handler integration tests 9/9 and API tests 4/4 passed against real PostgreSQL.
