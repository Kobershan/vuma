# STAGE 19 — CRM: 360° Customer View, Leads, Opportunities, Activities, Segments, Consent

**Status:** COMPLETE (2026-09-10) · **Depends on:** 10 · **Reference reading:** `docs/DATA_MODEL.md` §1–§3 (mandatory columns, types, schema conventions), `docs/CONVENTIONS.md` §1–§5, `docs/TESTING.md` §3, `docs/SECURITY.md` §1 (principals), `docs/LICENSING.md` §1–§4 (enforcement ladder, read-only), `docs/DECISIONS.md` ADR-149, **ADR-015** (customer record ownership — see §Overview), **ADR-090** (POPIA data retention), **CLAUDE.md** §7 rule 3 (mandatory columns), §7 rule 8 (soft delete), §7 rule 9 (timestamptz), §7 rule 16 (telemetry whitelist).

## Task index

| ID | TYPE | TITLE | DEPENDENCIES | STATUS |
|---|---|---|---|---|
| 19-MAP-01 | ARCHITECTURE | Stage-specific architecture decomposition and implementation task map | Stage dependencies in header | COMPLETE |
| [TASK-19-001](../tasks/TASK-19-001-crm-vertical.md) | DOMAIN / APPLICATION / INFRASTRUCTURE / API | CRM vertical | Stage 10 | COMPLETE |
| [TASK-19-002](../tasks/TASK-19-002-crm-verification.md) | TEST / DOCUMENTATION | CRM verification | TASK-19-001 | COMPLETE |

This is a planning gate, not an implementation task. Before this stage is selected, replace it with independently executable task files using the canonical template in `docs/tasks/README.md`.

## Objective

Stage 10 sold things. Stage 19 decides **who bought them and why** — and gives the shop the customer intelligence to sell smarter next time. CRM owns the relationship layer *around* a customer record that it does **not** own. The canonical customer identity record lives in Stage 06's master-data (`Customer` in the `catalog` schema, owned by the `master-data` stage and carried through `VumaRetail.Domain.Catalog`). CRM layers **leads, opportunities, activities, segments, and consent** on top of that identity, keyed by the customer's UUID.

This separation matters: CRM must never create, rename, or merge a `Customer` — it only references one. When Stage 19 needs to know who a lead became, it points at the `Customer.id` that Stage 06 allocated.

### What this stage owns

- **Lead** — an unqualified prospect. A lead is not a customer; it is a candidate that may convert into one through a documented process.
- **Opportunity** — a qualified lead with a deal, an expected value, a stage, and a probability. An opportunity is owned by a lead (or created directly from an existing customer).
- **Activity** — every logged interaction with a lead, opportunity, or customer: call, email, meeting, note, visit. Activities are append-only and immutable once recorded.
- **Segment** — a grouping of customers and leads for targeting. Segments are either static (explicit member list) or dynamic (a saved query evaluated at read time).
- **Consent** — a customer's opt-in/opt-out record for communications, marketing, and data processing. POPIA-compliant: captured, withdrawable, and expirable, with a full audit trail.
- **360° customer view** — an aggregated read model joining the customer record (Stage 06) with their leads, opportunities, activities, segment memberships, and consent state. This is a query-side projection, not a write-side entity.

### What this stage does **not** own

- **The customer identity record itself.** Owned by Stage 06 (`catalog.Customer`). CRM references it; it never creates, modifies, or deletes it.
- **Financial data.** AR balances, credit limits, and invoices are Stage 07 / 10b territory.
- **Loyalty points, tiers, or rewards.** Stage 20.
- **Campaign execution or marketing automation.** Stage 22.
- **The WhatsApp/email assistant.** Stage 22b.
- **Any write that would alter the customer record.** If Stage 19 needs a customer to exist, it requires one to already exist (Stage 06).

## Deliverables

**`crm` module** (schema `crm`, six tables)

- `Lead` — `FirstName`, `LastName`, `Email`, `Phone`, `Company`, `Source` (e.g. walk-in, web, referral, import), `Status` (`New`, `Contacted`, `Qualified`, `Converted`, `Disqualified`, `Dead`), `AssignedTo`, `StoreId`, `CreatedAt`, `ConvertedAt` (nullable, set when `Status` becomes `Converted`). Unique natural key on `(Email, StoreId)` where email is present.
- `Opportunity` — `Title`, `Description`, `ExpectedValue` (`Money`, ZAR), `Stage` (`Prospecting`, `Qualification`, `Proposal`, `Negotiation`, `Won`, `Lost`), `Probability` (`byte` 0–100), `CloseDate` (nullable), `LossReason` (nullable), `ParentLeadId` (nullable FK to `Lead` — an opportunity may also be created directly against an existing customer), `CustomerId` (nullable FK to `catalog.Customer`), `AssignedTo`, `StoreId`. Won opportunities reference the `CustomerId` they converted.
- `Activity` — `ActivityType` (`Call`, `Email`, `Meeting`, `Note`, `Visit`, `SMS`), `Direction` (`Inbound`, `Outbound`), `Subject`, `Body` (nullable, long text), `HappenedAt`, `DurationMinutes` (nullable), `ParentLeadId` (nullable), `ParentOpportunityId` (nullable), `CustomerId` (nullable), `CreatedBy`, `StoreId`. Append-only: no update path beyond `UpdatedAt`/`UpdatedBy` metadata. Immutability enforced by a domain invariant.
- `Segment` — `Name`, `Description`, `SegmentKind` (`Static`, `Dynamic`), `QueryExpression` (nullable, JSON-ish filter criteria for dynamic segments), `IsActive`, `StoreId`.
- `SegmentMember` — join table: `SegmentId`, `MemberTypeId` (`Customer` or `Lead`), `MemberId` (UUID, polymorphic reference), `AddedAt`, `AddedBy`. Composite PK on `(SegmentId, MemberTypeId, MemberId)`. Static segments populate this explicitly; dynamic segments are evaluated at read time and never persist here.
- `Consent` — `ConsentType` (`MarketingEmail`, `MarketingSms`, `MarketingPush`, `DataProcessing`, `ThirdPartySharing`, `Profiling`), `State` (`Given`, `Withdrawn`, `Expired`, `NotAsked`), `GrantedAt` (nullable), `WithdrawnAt` (nullable), `ExpiresAt` (nullable), `WithdrawalReason` (nullable), `CapturedAt`, `CapturedBy`, `CustomerId`, `StoreId`. One row per `(ConsentType, CustomerId)` pair.

**Read model** — `Customer360View` is a query-side projection materialised by a read service (`ICustomer360ViewService`). It aggregates: the `catalog.Customer` base record, counts of leads/opportunities/activities, segment names, and the current state of each consent type. Materialised on read (no write-side materialisation) so it always reflects the latest data.

**Ports** (`Application.Abstractions.Crm`)

- `ILeadService` — `CreateLead`, `UpdateLead`, `ConvertLeadToCustomer`, `AssignLead`, `GetLead`, `ListLeads`.
- `IOpportunityService` — `CreateOpportunity`, `UpdateStage`, `WinOpportunity`, `LoseOpportunity`, `GetOpportunity`, `ListOpportunities`.
- `IActivityService` — `LogActivity`, `GetActivitiesForLead`, `GetActivitiesForOpportunity`, `GetActivitiesForCustomer`.
- `ISegmentService` — `CreateSegment`, `UpdateSegment`, `EvaluateDynamicSegment`, `GetMembers`, `AddStaticMember`.
- `IConsentService` — `GiveConsent`, `WithdrawConsent`, `GetConsentState`, `IsConsentValid`.
- `ICustomer360ViewService` — `GetView(Guid customerId)`.

**Commands** — `CreateLeadCommand`, `UpdateLeadCommand`, `ConvertLeadCommand`, `CreateOpportunityCommand`, `UpdateOpportunityStageCommand`, `LogActivityCommand`, `CreateSegmentCommand`, `GiveConsentCommand`, `WithdrawConsentCommand`. All through the dispatcher.

**Queries** — `GetLeadQuery`, `ListLeadsQuery`, `GetOpportunityQuery`, `ListOpportunitiesQuery`, `GetActivitiesQuery`, `GetSegmentMembersQuery`, `GetConsentStateQuery`, `GetCustomer360ViewQuery`.

**Integration with what already exists**

- **Stage 06 / `catalog`** — `ConvertLeadCommand` creates a `catalog.Customer` record (or requires one to exist) and sets `Lead.ConvertedAt`. The customer UUID flows into all downstream CRM references.
- **Stage 10** — opportunities may be created against customers who bought through the POS. Stage 10's `PriceResolver` may reference a customer's segment for targeted pricing (extension point, not required at v1).
- **Stage 20** — loyalty membership is keyed off the `catalog.Customer` record that CRM helps maintain. Stage 20 consumes `CustomerId`, `SegmentMember` membership, and `ConsentState` to determine eligibility to earn/burn (see Stage 20 doc).
- **Stage 22** — campaigns reference segments and consent state. Segment membership and consent queries are the contract Stage 22 consumes.
- **Stage 07 / Finance** — no financial posting from CRM. CRM raises domain events only.

## Business rules

1. **A lead is not a customer until converted.** A lead may not be referenced by any financial document. Only a converted lead's resulting `Customer` may be used in Stage 07/10b flows.
2. **Conversion is atomic and one-way.** `ConvertLeadCommand` (a) creates or requires a `catalog.Customer`, (b) sets `Lead.Status = Converted` and `Lead.ConvertedAt = now`, (c) creates an `Activity` of type `System` noting the conversion. Once `Converted`, a lead may not be re-opened. The customer UUID generated at conversion is the source of truth referenced everywhere afterward.
3. **An opportunity's value is `Money` in the store's currency.** Bound to the document currency (Stage 07 rule 10).
4. **A `Won` opportunity must have a `CustomerId`.** A won opportunity ties back to the customer it converted, or to an existing customer if it was created directly. A won opportunity without a `CustomerId` is refused with `422 OPPORTUNITY_MISSING_CUSTOMER`.
5. **A `Lost` opportunity requires `LossReason`.** An opportunity may not be marked lost without a reason recorded for reporting.
6. **Activities are append-only.** An `Activity` may never be updated or deleted beyond the mandatory `updated_at`/`updated_by` metadata stamps. Deleting an interaction record is a compliance violation (POPIA §9 — records must be retrievable for the retention period).
7. **Segment membership is either static or dynamic, never both.** A `Static` segment's members are in `SegmentMember`; a `Dynamic` segment is evaluated at read time from its `QueryExpression` and `SegmentMember` rows for it are refused on write.
8. **Consent is per-type and per-customer.** There is exactly one row per `(ConsentType, CustomerId)`. A `Given` consent may be withdrawn at any time; withdrawal is immediate and takes effect on the next evaluation (no grace period for marketing). A `Given` consent may expire if `ExpiresAt` is set and passes.
9. **Withdrawal is immediate and irrevocable.** `WithdrawConsentCommand` sets `State = Withdrawn` and `WithdrawnAt = now`. There is no "undo" — re-consenting creates a new `Consent` row with `State = Given` and a fresh `GrantedAt`. The audit trail preserves both.
10. **POPIA retention.** Consent records and activity records are retained for the period required by POPIA and the tenant's stated privacy policy (minimum 12 months from the last interaction). After retention, records are anonymised (PII stripped, `Consent` state set to `Expired`, `Activity.Body` cleared) rather than hard-deleted — soft delete with `deleted_at` plus an `AnonymisedAt` timestamp.
11. **A dynamic segment query must not reference a `SegmentMember` row.** Dynamic queries evaluate against the customer/lead attributes directly. Mixing static and dynamic membership in the same segment is refused at creation time.
12. **The 360° view is read-only.** It is a projection. Nothing writes to it; nothing commands against it.

## Consent capture / withdrawal rules (POPIA)

Given this is a South African retail platform, POPIA (Protection of Personal Information Act) compliance is non-negotiable:

- **Purpose limitation.** Each `ConsentType` must be tied to a declared processing purpose. A marketing-email consent may not be reused for data-processing or profiling.
- **Voluntariness.** Consent must be freely given; a pre-ticked box is not consent. `GiveConsentCommand` requires an affirmative action marker (`Source` field recording the UI element or form that captured it).
- **Specificity.** A blanket "I agree to everything" is not valid. Each `ConsentType` is captured separately.
- **Withdrawal ease.** `WithdrawConsentCommand` must be as easy to invoke as `GiveConsentCommand`. No friction differential.
- **Audit trail.** Every consent state change records `CapturedBy`, `UpdatedBy`, and a `platform.audit_entries` row (Stage 01's `AuditInterceptor`). The audit trail is immutable and retained for the full retention period.
- **Children.** Consent for a customer under 18 (if such data is captured) requires a parent/guardian consent marker. This is recorded on the `Consent` row as `RequiresParentalConsent = true` and enforced at `GiveConsentCommand` validation time.

## API surface (internal service interfaces)

Other Vuma modules call CRM through these interfaces — this is the **internal** contract, not the public API (that is Stage 20).

| Interface | Method | Returns | Notes |
|---|---|---|---|
| `ILeadService` | `ConvertLeadAsync(ConvertLeadCommand, CancellationToken)` | `ConvertLeadResult` (new/existing `CustomerId`, `LeadStatus`) | Atomic. Called from Stage 06 when a master-data customer is identified as a known lead. |
| `ILeadService` | `GetLeadWithOpportunitiesAsync(Guid leadId, CancellationToken)` | `LeadDetailDto` | Used by the 360° view. |
| `IOpportunityService` | `CreateOpportunityAsync(CreateOpportunityCommand, CancellationToken)` | `OpportunityDto` | Validates `CustomerId` exists if provided. |
| `IOpportunityService` | `UpdateStageAsync(Guid opportunityId, OpportunityStage newStage, CancellationToken)` | `OpportunityDto` | Enforces stage-transition rules (e.g. `Lost` may not transition back to `Qualification`). |
| `IActivityService` | `LogActivityAsync(LogActivityCommand, CancellationToken)` | `ActivityDto` | Append-only. Creates an immutable record. |
| `ISegmentService` | `IsMemberAsync(Guid segmentId, Guid memberId, MemberType type, CancellationToken)` | `bool` | Evaluates static membership and dynamic query. |
| `ISegmentService` | `EvaluateDynamicSegmentAsync(Guid segmentId, CancellationToken)` | `IReadOnlyList<Guid>` (member IDs) | Evaluates the dynamic query. Called by Stage 22 for campaign targeting. |
| `IConsentService` | `GetConsentStateAsync(Guid customerId, ConsentType type, CancellationToken)` | `ConsentState` (Given/Withdrawn/Expired/NotAsked + timestamps) | **Critical contract for Stage 20.** Returns whether a customer may be marketed to and whether loyalty messaging is permitted. |
| `IConsentService` | `IsConsentValidAsync(Guid customerId, ConsentType type, CancellationToken)` | `bool` | Short-circuit: `State == Given && (ExpiresAt == null || ExpiresAt > now)`. |
| `ICustomer360ViewService` | `GetViewAsync(Guid customerId, CancellationToken)` | `Customer360ViewDto` | Aggregation query. Called by Stage 10 (pricing), Stage 20 (eligibility), Stage 22 (targeting), and the desktop POS. |

**Error contract.** All commands return `ProblemDetails` per `docs/API_STANDARDS.md` §5. Domain-specific codes: `LEAD_ALREADY_CONVERTED`, `OPPORTUNITY_MISSING_CUSTOMER`, `SEGMENT_DYNAMIC_WRITE_NOT_ALLOWED`, `CONSENT_NOT_GIVEN`, `ACTIVITY_IMMUTABLE`.

## Non-functional requirements

1. **Consent audit trail.** Every consent state change produces an immutable `platform.audit_entries` row. This audit trail is never modifiable and is retained for the full POPIA retention period. An architecture test asserts that no code path can delete or alter an audit entry.
2. **Activity immutability.** Once an `Activity` row is committed, its `Body`, `Subject`, `HappenedAt`, and `ActivityType` are immutable. Only `UpdatedAt`/`UpdatedBy` metadata may change (and even those are controlled by the persistence layer).
3. **Data retention.** Consent records: retain for the period declared in the tenant's privacy policy (minimum 12 months from last interaction) then anonymise. Activity records: retain for the same period. Segment membership: retain indefinitely (membership history is analytical).
4. **360° view latency.** `GetViewAsync` must return within 200 ms for a customer with up to 10 000 activities and 500 opportunities. Materialise the read model on write if this is not met.
5. **POPIA data subject request.** A `GetConsentAuditTrailQuery` must produce a complete, chronological list of every consent change for a customer, suitable for export to a data subject on request.
6. **Telemetry.** Only aggregate counts (leads created today, opportunities won this month, consents given/withdrawn) are emitted — never personal data. See `CLAUDE.md` §7 rule 16.
7. **Offline tolerance.** A till may log an `Activity` against a customer without network connectivity; the activity queues and syncs to the store server on reconnection (Stage 04 sync protocol). The `Activity` is stamped with an HLC (`sync_stamp`) per `docs/SYNC_AND_BACKUP.md` §2.

## Tests

### Unit tests (`tests/VumaRetail.UnitTests/Crm/`)

1. **Lead → Customer conversion** (`LeadConversionTests`)
   - Convert a new lead creates a customer with matching identity fields.
   - Converted lead sets `Status = Converted` and `ConvertedAt`.
   - Converting an already-converted lead throws `LeadAlreadyConvertedException`.
   - Converted lead cannot be re-opened.
   - Customer UUID from conversion is returned and referenced consistently.
   - Conversion creates an `Activity` of type `System`.

2. **Segment membership evaluation** (`SegmentMembershipTests`)
   - Static segment: `AddStaticMember` makes a member queryable; `IsMemberAsync` returns true.
   - Dynamic segment: `EvaluateDynamicSegmentAsync` returns customers matching the query expression.
   - Mixed segment (static + dynamic) is refused at creation (`SegmentMixedKindException`).
   - Inactive segment returns no members.
   - Dynamic segment evaluated against customer attributes (age-of-data, purchase recency).

3. **Consent state transitions** (`ConsentStateMachineTests`)
   - `NotAsked → Given` via `GiveConsentCommand`.
   - `Given → Withdrawn` via `WithdrawConsentCommand`.
   - `Given → Expired` when `ExpiresAt` passes.
   - `Withdrawn → Given` (re-consent creates a new row).
   - `IsConsentValidAsync` returns false for `Withdrawn`, `Expired`, and `NotAsked`.
   - `IsConsentValidAsync` returns true for `Given` with a future `ExpiresAt`.
   - Withdrawing consent is immediate (no grace period).
   - Consent per-type exclusivity: exactly one row per `(ConsentType, CustomerId)`.

4. **Activity immutability** (`ActivityImmutabilityTests`)
   - Logged activity `Body`/`Subject` cannot be changed through the domain model.
   - `UpdatedAt`/`UpdatedBy` may change (metadata only).
   - Deleting an activity is refused (`ActivityImmutableException`).

### Integration tests (`tests/VumaRetail.IntegrationTests/Crm/`)

5. **360° view aggregation** (`Customer360ViewIntegrationTests`)
   - A customer with leads, opportunities, activities, segment memberships, and consent state — `GetViewAsync` returns all counts and states correctly.
   - Verify that converting a lead surfaces it in the 360° view as both lead (historical) and customer (current).
   - Verify consent withdrawal immediately affects the view's `MarketingEmail` consent state.

6. **Command handlers against real Postgres** (`CrmCommandHandlerIntegrationTests`)
   - Each command handler tested against a real database (Testcontainers), real migrations, no mocked DB.
   - `CreateLeadCommand` → row in `crm.leads` with correct tenant scoping.
   - `ConvertLeadCommand` → lead updated, customer created, activity logged, all in one transaction.

### Architecture tests (existing suite, `tests/VumaRetail.ArchitectureTests/`)

7. **`crm` names no GL account** — new architecture test asserting the `crm` module's Application assembly references no finance types (per rule 12 pattern).
8. **`crm` entities registered in the replication registry** — verify `Lead`, `Opportunity`, `Activity`, `Segment`, `Consent` appear in the `docs/SYNC_AND_BACKUP.md` §3 registry (per Stage 04 pattern).
9. **Commands classified for read-only guard** — every command in `crm` is declared read or write (per the pattern in Stage 04b).

## Open questions / hooks left for other stages

- **Stage 10 (sales & promotions)** — whether segment-based pricing is a v1 feature or deferred to a later stage. The extension point exists (`ICustomer360ViewService` is called from pricing), but segment-qualified pricing is not required at v1. Hook: `IPriceResolver` may accept a `CustomerId` and consult segments in a future amendment (see `docs/stages/STAGE-10-sales-promotions.md` line 227).
- **Stage 22 (marketing automation)** — campaign execution must consume segments and consent. Stage 19 does not build the campaign engine; it only exposes the query. Hook: `ISegmentService.EvaluateDynamicSegmentAsync` is the contract Stage 22 needs.
- **Stage 20 (loyalty)** — `IConsentService.IsConsentValidAsync` is the critical contract for determining whether a loyalty message may be sent to a member. Stage 19 must expose this before Stage 20 can gate marketing-facing loyalty actions.
- **Stage 29 (reporting)** — CRM aggregate counts feed the KPI cube. Stage 19 must emit the correct counters; Stage 29 consumes them.
- **POPIA retention period** — the exact number of months is tenant-configurable (declared in the privacy policy). This document assumes a minimum of 12 months; the tenant setting overrides it. Hook: `IRetentionPolicyService` (to be defined in Stage 04b or a data-protection stage) provides the configurable period.
- **Customer duplicate detection / merge** — Stage 19 may eventually need a merge command (two leads → one customer). Not in v1. Hook: `IMergeService` interface reserved for a future stage.

## Notes for later stages

- **Stage 10b (accounts, lay-by, stokvels)** may reference a customer's CRM segment for preferential lay-by terms. Segment membership is the hook.
- **Stage 20 (loyalty)** — customer identity, segment eligibility, and consent are the three Stage-10-supplied contracts. See `docs/stages/STAGE-20-loyalty-public-api.md` for the explicit contract definitions.
- **Stage 22b (conversational commerce)** — the WhatsApp/email assistant may query the 360° view before responding. Consent state gates whether it may reference any customer data at all.
- **Stage 14b (field sales)** — reps need lead/opportunity access. The rep's territory scoping will be a query filter on `ListLeadsQuery`.

---

## Testing summary

| Category | Tests | Status |
|---|---|---|
| Unit — lead conversion | 6 | TO BE WRITTEN |
| Unit — segment membership | 5 | TO BE WRITTEN |
| Unit — consent state machine | 8 | TO BE WRITTEN |
| Unit — activity immutability | 3 | TO BE WRITTEN |
| Integration — 360° view | 3 | TO BE WRITTEN |
| Integration — command handlers | 6+ | TO BE WRITTEN |
| Architecture | 3 | TO BE WRITTEN |
| **Total unit/integration** | **31+** | — |

Required coverage: ≥ 80% line on Domain(`crm`) + Application(`crm`) — see `docs/TESTING.md` §5 and `CLAUDE.md` §8.
