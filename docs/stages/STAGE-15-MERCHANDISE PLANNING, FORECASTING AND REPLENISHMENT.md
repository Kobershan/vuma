# VUMA RETAIL — AUTONOMOUS STAGE 15 IMPLEMENTATION SUPERVISOR

You are the autonomous implementation engineer responsible for completing **Stage 15 — Merchandise Planning, Forecasting & Replenishment** in the Vuma Retail repository.

You are not a planning-only assistant. You must inspect the repository, implement the required code, create and update tests, run the tests, diagnose failures, fix your implementation, validate the relevant GitHub Actions/CI checks, update required documentation/evidence, and commit completed work.

Do not merely tell me what should be changed.

Do not stop after producing an implementation plan.

Do not ask for permission for normal repository operations.

Do not ask me to run commands you can run yourself.

Do not claim that a command, test, build, migration, guard, coverage run, or workflow check passed unless you actually ran it and observed a successful exit result.

Do not fabricate test results.

---

# 1. PRIMARY OBJECTIVE

Complete these tasks, **in this exact dependency order**:

1. `TASK-15-01` — Demand history and forecasting engine
2. `TASK-15-02` — Safety stock, reorder points and open-to-buy
3. `TASK-15-03` — MRP/DRP replenishment suggestions and requisition/transfer integration
4. `TASK-15-04` — Markdown planning and Stage 15 verification

Treat each task as an independent implementation checkpoint.

**Do not begin the next task until the current task has been implemented, tested, reviewed against its acceptance criteria, and brought to a green checkpoint.**

TASK-15-04 additionally performs the final verification of the entire Stage 15 module.

---

# 2. AUTHORITATIVE SOURCES

Before changing code, inspect the repository and locate the exact files rather than assuming paths that may have changed.

At minimum, read:

* `CLAUDE.md`
* applicable `AGENTS.md` files
* `docs/ARCHITECTURE-INDEX.md`, if present
* `docs/CURRENT.md`, if present
* `docs/stages/STAGE-15-merchandise-planning.md`
* the exact task file for the task currently being implemented
* `docs/DATA_MODEL.md`
* `docs/TESTING.md`
* `docs/CONVENTIONS.md`
* only the ADRs and previous-stage documents referenced by the current task
* existing implementations from analogous modules where the task explicitly references an existing pattern

Do not load the entire repository blindly.

Use repository search to locate the relevant interfaces, entities, commands, tests, registrations and conventions.

The task document and Stage 15 architecture define WHAT must exist and WHY.

Existing architecture/conventions define HOW Vuma implements those concepts.

Do not invent a new local architectural style merely because it is convenient.

If an existing implementation already establishes a Vuma pattern for something such as:

* entity configuration
* tenant scoping
* soft deletion
* command handlers
* repositories
* scheduled jobs
* permissions
* entitlement checks
* metering
* published cross-module ports
* snapshot/versioning
* OpenAPI controllers
* Testcontainers
* architecture tests
* replication metadata

reuse that pattern.

If requirements genuinely contradict an authoritative architecture rule, mark that specific issue as:

`NEEDS_ARCHITECTURAL_CLARIFICATION`

Do not silently invent a new architectural decision.

Continue all non-blocked work, but do not mark a task complete if a blocking architectural contradiction remains.

---

# 3. REPOSITORY SAFETY

Before editing:

```text
git status
git branch --show-current
git log -5 --oneline
```

Understand the current branch/worktree before modifying anything.

Never use destructive cleanup merely to make the repository look clean.

Do NOT use:

```text
git reset --hard
git clean -fd
git checkout . 
```

against user work.

Do not overwrite unrelated uncommitted changes.

Only stage and commit files belonging to your implementation.

Do not casually reformat unrelated files.

Do not refactor unrelated modules while implementing Stage 15.

---

# 4. IMPLEMENTATION METHOD

For EACH task:

## Step A — Discover

Read the task and referenced architecture.

Locate:

* affected source code
* analogous existing implementations
* current registrations
* existing test infrastructure
* migrations
* controllers/OpenAPI conventions
* scheduled-job conventions
* permissions
* entitlement implementation
* replication conventions
* relevant GitHub workflow commands

Then write a short internal implementation checklist and begin work immediately.

Do not stop to ask permission.

## Step B — Implement

Implement the smallest coherent solution satisfying:

* task requirements
* task acceptance criteria
* task edge cases
* security requirements
* Stage 15 business rules
* existing architecture

Do not implement later-task functionality early unless a minimal seam is explicitly needed.

## Step C — Add tests

Tests are part of the implementation, not an optional final step.

Create all tests necessary to prove the task requirements.

Use the repository's existing testing style.

Do not weaken existing assertions to make your code pass.

If an existing test fails because your implementation violates existing behavior, fix your implementation.

Only change an existing test when you have verified from authoritative documentation that the test itself is outdated or incorrect.

When changing such a test, document exactly why.

## Step D — Run targeted tests

Run the smallest relevant test projects/classes first so failures are quick to diagnose.

Fix every failure caused by your work.

## Step E — Run impacted suites

After targeted tests pass, run the broader Domain/Application/Infrastructure/Web/integration tests affected by the change.

## Step F — Review acceptance criteria manually

Go through every acceptance criterion and edge case in the task one-by-one.

For each one identify the code/test proving it.

Do not rely on "all tests passed" as proof that a missing acceptance criterion exists.

## Step G — Green checkpoint

Only when the task is green:

* update task evidence/work log according to repository conventions
* update CURRENT/progress metadata if that is part of the existing workflow
* inspect `git diff`
* inspect `git status`
* commit only the current task's work

Suggested commit convention:

```text
feat(stage-15): complete TASK-15-01 demand forecasting
feat(stage-15): complete TASK-15-02 replenishment parameters
feat(stage-15): complete TASK-15-03 replenishment suggestions
feat(stage-15): complete TASK-15-04 markdown planning and verification
```

Follow the repository's existing commit convention if it differs.

Then continue automatically to the next task.

---

# 5. TASK-15-01 — DEMAND HISTORY AND FORECASTING

Implement the complete task document.

The essential architecture is:

## DemandHistory

Create the `planning.demand_history` read model.

It must:

* derive from the authoritative stock/demand source specified by the Stage/task architecture
* aggregate `SaleIssue` movements into fixed periods
* default to weekly periods
* allow the existing tenant configuration mechanism to control period grain where required
* group by item/variant × location × period
* remain rebuildable from the authoritative ledger
* never become a second source of truth
* remain tenant/company scoped
* use the existing Vuma soft-delete/replication conventions where applicable

The rollup must be idempotent.

Running it twice for the same source data must NOT double quantities.

Periods with no sales inside an otherwise active series must be represented/handled consistently so forecasting mathematics does not accidentally shift time.

Test zero-history and gap-period scenarios.

## DemandForecast

Create `planning.demand_forecasts`.

A forecast must retain:

* item/variant
* location
* forecast period
* quantity
* forecast method
* MAPE
* bias if required by the stage model
* generation timestamp
* version
* other standard Vuma tenant/company/audit fields required by convention

Closed-period forecasts are immutable snapshots.

Re-running a closed period must create:

```text
Version = previous maximum version + 1
```

It must NOT update Version 1 in place.

Both versions must remain queryable.

Concurrency must not accidentally create duplicate identical version numbers.

Use the repository's established snapshot/version approach.

## IForecastEngine

It must remain:

* pure
* deterministic
* synchronous
* repository-free
* database-free
* I/O-free

Implement the three strategies behind the interface:

1. `MovingAverageForecastStrategy`
2. `ExponentialSmoothingForecastStrategy`
3. `SeasonalNaiveForecastStrategy`

Do not duplicate the whole forecast engine three times.

Use the architecture's intended strategy seam.

Method selection must support an explicit method parameter now so TASK-15-02 can later supply `ReplenishmentParameter.ForecastMethod`.

Support tenant default selection through the existing configuration mechanism where the task requires it.

## Forecast mathematics

Tests must use hand-computed inputs and expected results.

For moving average, test a known fixed window.

For exponential smoothing, test a known alpha.

For seasonal naive, test at least two complete cycles with a known seasonal relationship.

Test MAPE against hand-computed expected values.

Define handling of an actual value of zero explicitly; do not allow divide-by-zero/NaN to leak into persisted results.

Test:

* empty series
* one observation
* insufficient seasonality
* normal series
* gaps/zero-demand periods

Insufficient input must produce the architecture's defined low-confidence result rather than throwing or pretending confidence exists.

## Scheduled jobs

Implement:

* nightly demand-history rollup
* configurable forecast run, default weekly

Follow the scheduler pattern already used in Vuma.

Do not introduce another scheduling framework.

## Read API

Implement the required forecast/history read endpoints.

Enforce:

`planning.forecast.view`

Use Vuma's existing controller/result/error conventions.

Ensure endpoints are visible in `/openapi/v1.json`.

## TASK-15-01 mandatory tests

At minimum prove:

* 90 seeded days of `SaleIssue` data aggregate into the mathematically correct weekly totals
* rollup twice does not double totals
* moving average known answer
* exponential smoothing known answer
* seasonal naive known answer
* MAPE known answer
* empty input
* one-point input
* missing periods
* insufficient seasonal cycles
* forecast persistence
* Version 1 retained after Version 2 generated
* migration Up
* migration Down
* permissions
* OpenAPI discovery

Do not move to TASK-15-02 until these are green.

---

# 6. TASK-15-02 — SAFETY STOCK, REORDER POINTS, ABC/XYZ AND OTB

Implement:

* `planning.replenishment_parameters`
* `planning.abc_xyz_classifications`
* `planning.safety_stock_calculations`
* `planning.open_to_buy_budgets`
* `ISafetyStockCalculator`
* classification job
* upsert commands/endpoints
* required readers/ports
* DI
* permissions
* migrations
* tests

## ISafetyStockCalculator

Must be pure.

No repository or I/O inside it.

Inputs include the architecture's:

* lead-time demand mean
* demand variance
* service-level target
* history length
* lead time
* fallback buffer inputs

Convert service-level percentage to z-score during calculation.

Do NOT store the z-score as tenant configuration.

Test a known example such as approximately:

```text
95% => z ≈ 1.645
```

Use the precision/rounding convention already established by the codebase.

## Minimum-history fallback

Default minimum history:

```text
8 weeks
```

unless the repository's authoritative configuration specifies another value.

Below the minimum history threshold use the required fallback:

```text
LeadTimeDays × AverageDailyDemand × BufferPercentage
```

and flag the result:

```text
LowConfidence
```

Test:

* zero history
* one below threshold
* exact threshold
* one above threshold
* zero variance

No NaN/infinity/divide-by-zero.

## Reorder horizon

If the forecast horizon cannot cover:

```text
lead time + review period
```

the calculation must refuse with a stated domain/application reason.

Do not silently return zero.

## ABC/XYZ

Classification is a scheduled snapshot.

Reading it must never recalculate classification.

Existing snapshots remain unchanged when new sales occur.

Use deterministic category boundaries and the configured thresholds.

Add table-driven tests for known A/B/C and X/Y/Z classifications.

## Open to Buy

Implement:

```text
Remaining = Planned - Committed
```

Committed must be read live from the existing procurement sources through published ports.

Do not directly couple the planning module to another module's DbContext unless that is already explicitly the Vuma cross-module pattern.

Cancelled requisitions/orders must no longer count as committed.

Test:

* no commitments
* multiple commitments
* cancelled commitment
* over-committed budget

Critically:

**Open-to-buy is a warning/flag, never a blocker.**

## Permissions

Implement/register the exact relevant planning permissions, including:

* `planning.parameters.manage`
* `planning.classification.view`
* `planning.otb.manage`

Follow existing separation between read/write authorities.

## TASK-15-02 mandatory tests

Prove:

* variance-based safety stock against hand-computed values
* known service-level conversion
* minimum-history boundary
* low-confidence fallback
* zero history
* zero variance
* reorder horizon refusal
* deterministic ABC/XYZ classification
* snapshot immutability
* OTB arithmetic
* cancelled requisition behavior
* over-budget is allowed but flagged
* real seeded procurement integration
* migration Up/Down
* permission enforcement
* OpenAPI entries where applicable

Do not proceed until green.

---

# 7. TASK-15-03 — MRP/DRP REPLENISHMENT

Implement:

* `ReplenishmentSuggestion`
* `IReplenishmentEngine`
* scheduled replenishment job
* scheduled backorder reattempt hook
* accept
* amend-and-accept
* reject
* expiry behavior
* published module writer ports
* controller/endpoints
* permissions
* migration
* tests

## Engine boundaries

`IReplenishmentEngine` decides/proposes.

It does NOT automatically commit procurement or stock movement.

Possible reasons:

* `BelowReorderPoint`
* `ForecastDrivenTopUp`
* `TransferSurplus`

A suggestion only creates a downstream document when explicitly accepted.

## Procurement path

Acceptance must create a real existing Stage 12:

`PurchaseRequisition`

It must use Stage 12's existing command/port.

Do not create a second planning-specific procurement aggregate.

The requisition remains supplier-free:

```text
partner_id = null
```

where that is Stage 12's required shape.

## Transfer path

Prefer an eligible `TransferSurplus` over procurement when the architecture says it is valid.

Read sister-company availability only through the approved group availability projection.

That projection is read-only and `AsAt` stamped.

Do not expose or depend on sister-company cost/margin information beyond the existing availability contract.

## CompanyLink is mandatory

Before generating a cross-company transfer suggestion verify an:

* active
* correctly scoped
* valid `CompanyLink`

The check occurs during suggestion GENERATION.

No valid link means:

```text
NO TransferSurplus suggestion exists.
```

Do NOT generate it and later reject it at acceptance.

Create an explicit integration test proving absence of the suggestion.

## No cross-database transaction

Never create a transaction spanning multiple company databases.

Follow Vuma's existing cross-company architecture.

The requesting company writes only its own downstream document according to the existing Stage 08 pattern.

## Exactly-once acceptance

Accepting one suggestion twice must not create two requisitions/transfers.

Implement using existing idempotency/concurrency conventions.

The second request should either:

* return the existing accepted result
* no-op safely
* return the repository's standard conflict response

but must never create a duplicate document.

## Amend and accept

The amended quantity goes to the downstream document.

It must NOT mutate:

`SuggestedQuantity`

The suggestion must preserve the machine's original recommendation.

## Reject/expire

Rejected or expired suggestions create no rows attributable to the suggestion in:

* procurement
* inventory
* sales

Test this using actual downstream document counts/state, not just mocks.

## Transfer tie breaking

When several valid locations have surplus, selection must be deterministic.

If an existing architecture decision exists, use it.

If Stage 15 requires choosing a new irreversible business rule and no ADR exists, identify the decision for the Stage 15 ADR/evidence rather than hiding the choice.

## Backorder reattempt

Schedule/call the existing:

`ReattemptBackorderedAllocationsCommand`

Do not reimplement Stage 14's allocation logic.

## Permissions

Keep:

* `planning.suggestion.view`
* `planning.suggestion.accept`

separate.

## TASK-15-03 mandatory tests

Unit tests:

* below reorder point
* forecast-driven top-up
* transfer surplus preference
* no CompanyLink suppression
* deterministic source selection
* amend-and-accept
* accept twice
* rejected
* expired
* missing parameter for location
* OTB exceeded but still acceptable

Integration:

* run → suggestion → accept → real PurchaseRequisition
* supplier-free requisition
* run → transfer suggestion → real StockTransfer
* transfer ledger effects according to existing Stage 08 rules
* no CompanyLink produces NO transfer suggestion
* reject/expire produces no downstream document
* backorder reattempt scheduled integration
* permission enforcement
* migration Up/Down
* endpoints/OpenAPI

Do not continue until green.

---

# 8. TASK-15-04 — MARKDOWN PLANNING AND COMPLETE STAGE VERIFICATION

Implement:

* `MarkdownPlan`
* `MarkdownPlanLine`
* `IMarkdownPlanner`
* create
* approve
* activate step
* cancel
* amendment/versioning
* Stage 05 approval integration
* Stage 10 promotion integration
* controller/API
* permissions
* migrations
* DI
* final documentation
* seed data
* complete Stage 15 verification

## Markdown planner

Evaluate required signals including:

* ABC/XYZ
* sell-through
* days of supply
* configured thresholds

Test exactly:

* below threshold
* at threshold
* above threshold

Do not allow duplicate silent markdown plans for a SKU that already has an incompatible live plan.

## Approval

Approval MUST call Stage 05's existing `IApprovalService`.

Do not implement a local planning approval engine.

An approved plan that has not had a step activated must NOT create a sales promotion.

## Activation

Only activation produces/updates the promotion.

Call Stage 10 through:

`IPromotionWriter`

or the actual existing equivalent published port/command.

Never insert directly into `sales.promotions`.

Stage 10 remains the pricing authority.

## Live inputs

At the required authoring/activation point, use the architecture's live:

* resolved price
* average cost

Do not use an old cached value incorrectly.

Snapshot the decision inputs according to Vuma's audit conventions.

## Live amendment

A currently live plan is not edited in place.

Close/version the current plan according to architecture and create a new plan version.

The old live step/promotion remains historically intact.

## Cancellation

If a live plan's promotion must be stopped, call Stage 10's existing deactivation/update command.

Do not leave an orphaned promotion.

## TASK-15-04 mandatory tests

Prove:

* slow-moving C/Z SKU can create Draft plan
* threshold boundaries
* no promotion on Draft
* no promotion merely because Approved
* approval through `IApprovalService`
* activation creates real `sales.promotions`
* Stage 10 `ResolvePriceCommand` actually picks up the promotion
* unapproved plan has no pricing effect
* amendment creates new version
* existing version is not edited in place
* missed activation date activates on next valid scheduled run
* cancellation correctly deactivates via Stage 10
* duplicate/live-plan edge cases
* permission enforcement
* migration Up/Down

Then perform the COMPLETE Stage 15 exit verification.

---

# 9. TESTING POLICY

You are responsible for both NEW tests and EXISTING tests.

Do not only run the tests you created.

## During each task

Run targeted tests first, for example using the actual project names discovered in the solution:

```bash
dotnet test <UnitTestProject> -c Release --filter Planning
dotnet test <IntegrationTestProject> -c Release --filter Planning
```

Use actual project/filter names from this repository.

Do not blindly copy these example commands if the solution is structured differently.

Then run all affected test projects.

## At Stage 15 completion

Run:

```bash
dotnet build -c Release
dotnet test -c Release
```

or the repository's canonical solution equivalents.

The final build must have:

```text
0 errors
0 warnings
```

where the repository's Stage 15 exit criteria requires zero warnings.

Do not suppress legitimate warnings merely to achieve zero.

Fix them.

---

# 10. COVERAGE

Stage 15 requires at least:

```text
80% line coverage
```

for its new Domain + Application code.

Inspect the repository to determine the existing coverage tooling and canonical command.

Use that mechanism.

Do not introduce a second coverage framework unnecessarily.

Run the coverage check and inspect the resulting number.

If coverage is below the threshold:

1. identify untested meaningful branches
2. add behavioral tests
3. rerun coverage

Do not create useless line-touching tests solely to manipulate the metric.

---

# 11. DATABASE / MIGRATION TESTING

For every planning migration:

* compile it
* apply it
* exercise it
* reverse it

The repository's migration test infrastructure must prove `Down`.

Do not declare migrations complete because `dotnet ef migrations add` succeeded.

Check:

* indexes
* uniqueness/versioning constraints
* tenant/company scoping
* soft-delete conventions
* schema names
* required precision
* enum conversion conventions
* cross-schema FK prohibition
* replication metadata

There must be no cross-schema foreign key introduced by Stage 15.

---

# 12. GITHUB ACTIONS / CI VERIFICATION

This is mandatory.

Before declaring Stage 15 complete, inspect:

```text
.github/workflows/*.yml
.github/workflows/*.yaml
```

Also inspect any referenced:

* shell scripts
* PowerShell scripts
* reusable workflows
* composite actions
* build scripts
* test scripts

Determine which workflow jobs/checks apply to this repository and these files.

Create a checklist of the actual CI commands.

Examples may include:

* restore
* Release build
* unit tests
* integration tests
* Testcontainers/Postgres
* migration tests
* formatting
* coverage
* architecture tests
* OpenAPI tests
* dependency checks
* shell tests
* guards
* seed verification

Run the underlying commands locally whenever the environment permits.

Do not say:

`GitHub Actions should pass`

Instead prove as much of the workflow as possible.

For every applicable workflow check record:

```text
CHECK
COMMAND
RESULT
```

If a GitHub-only service/event cannot literally be reproduced locally, run the underlying build/test command it invokes and state precisely which orchestration layer could not be simulated.

Do NOT edit a workflow merely to make a failing codebase appear green.

Only modify workflow configuration when Stage 15 legitimately requires the workflow itself to know about a newly added project/check and that change is consistent with existing CI architecture.

---

# 13. EXISTING TEST COMPATIBILITY

After Stage 15's own tests pass, run the full existing test suite.

This is important because Stage 15 integrates with:

* inventory
* procurement
* warehouse
* orders
* sales
* approval
* permissions
* multi-company
* replication
* licensing/entitlements

A Stage 15 implementation is not green if it breaks an existing Stage 08/10/12/14 contract.

Investigate every regression.

Distinguish:

1. regression caused by Stage 15
2. pre-existing failure unrelated to Stage 15

For a pre-existing unrelated failure, capture the exact test name and failure output and do not alter unrelated code just to hide it.

For a regression caused by Stage 15, fix it before completion.

---

# 14. ARCHITECTURE TESTS

Run all existing architecture tests relevant to the changes.

Explicitly verify:

* Layering tests detect Planning
* no Domain → Infrastructure dependency
* no forbidden Application → Infrastructure dependency
* no cross-schema foreign key
* no GL-account ownership in Planning
* cross-module calls use published ports
* `IApprovalService` is the only markdown approval path
* Stage 10 remains pricing authority
* replication rules see every required entity
* tenant/company scoping conventions are followed
* soft-delete conventions are followed
* command classifications are correct
* entitlement checks follow existing module conventions

If an architecture test does not yet cover a Stage 15 invariant that the stage explicitly requires, add the appropriate architecture test rather than relying only on code review.

---

# 15. OPENAPI VERIFICATION

Start/use the Web project according to existing test infrastructure and verify `/openapi/v1.json`.

Every new endpoint required by Stage 15 must appear.

Check:

* route
* HTTP method
* summary
* response/error documentation
* authorization metadata where the project exposes it

Prefer automated OpenAPI integration assertions so future regressions are caught.

---

# 16. SECURITY / RBAC

Register and test all Stage 15 permissions required by the architecture, including:

* `planning.forecast.view`
* `planning.parameters.manage`
* `planning.classification.view`
* `planning.suggestion.view`
* `planning.suggestion.accept`
* `planning.otb.manage`
* `planning.markdown.propose`
* `planning.markdown.approve`

Do not collapse propose/approve or view/accept into one broad permission.

Test at least the sensitive write/action endpoints with authorized and unauthorized callers using existing security test infrastructure.

---

# 17. ENTITLEMENT AND METERING

By stage close verify the `MerchandisePlanning` entitlement/module flag is declared and enforced according to existing Vuma module conventions.

Implement/verify usage counters for:

* forecasts generated
* suggestions raised
* suggestions accepted
* markdown plans activated

Counts only.

Do not introduce financial semantics into usage metering.

---

# 18. REPLICATION / SYNC

For every new entity determine the correct existing replication scope.

Update the replication registry and tests.

Update:

`docs/SYNC_AND_BACKUP.md`

where required.

Do not guess replication semantics.

Follow analogous existing entities and Stage 15 architecture.

Make sure `ReplicationRulesTests` or equivalent detects the complete Stage 15 entity set.

---

# 19. SEED SCRIPT

Update `scripts/seed.sh` according to the Stage 15 exit checklist.

The seed must demonstrate real, connected scenarios including:

1. 90 days of SaleIssue history
2. generated demand history
3. generated forecast
4. below-reorder-point suggestion
5. accepted PurchaseRequisition
6. transfer-surplus suggestion
7. accepted StockTransfer
8. corresponding ledger effects using existing Stage 08 behavior
9. markdown plan
10. approval
11. active promotion
12. OTB over-commit flagged without blocking

Do not fake database rows that are supposed to be produced through existing application flows when the exit criteria explicitly require proof of integration.

---

# 20. FINAL GUARD PANEL

Discover and run the repository's actual commands for:

* `architecture-guard`
* `stock-availability-guard`
* `money-and-tax`

Do not invent command names.

Find their existing invocation mechanism.

Resolve findings attributable to Stage 15.

If a finding is intentionally accepted by the architecture, record it according to the repository's normal evidence mechanism.

---

# 21. FINAL DOCUMENTATION

TASK-15-04 must close Stage 15 documentation.

Review/update as required:

* `docs/DATA_MODEL.md`
* planning schema section / §4p
* schema inventory
* `docs/SYNC_AND_BACKUP.md`
* `docs/AGENTS.md`
* `docs/DECISIONS.md`
* `docs/PROGRESS.md`
* task work logs/evidence
* `docs/CURRENT.md` if used by the repository
* seed documentation if applicable

Write the Stage 15 ADR decisions required by the architecture, including the finalized decisions around:

* forecast method set/default
* safety-stock formula/default service-level
* OTB flags rather than blocks
* suggestion never auto-commits
* markdown planning calls Stage 10 and is not a second pricing authority
* any other Stage 15 decision the architecture explicitly requires at close

Do not create unnecessary ADRs for implementation details already governed by existing ADRs.

---

# 22. FINAL STAGE-15 VERIFICATION MATRIX

Before marking Stage 15 DONE, create a verification matrix in the task/stage evidence.

For every acceptance/exit criterion provide:

```text
Requirement
Implementation
Test/evidence
Result
```

Every line must be one of:

```text
PASS
FAIL
BLOCKED
NOT APPLICABLE
```

Do not use vague statuses such as "probably complete".

At minimum verify:

* Release build
* full tests
* Stage 15 tests
* ≥80% Domain/Application coverage
* migration Up
* migration Down
* OpenAPI
* permissions
* entitlement
* metering
* replication
* no posting rule
* no GL account
* no cross-schema FK
* seed scenarios
* architecture guard
* stock availability guard
* money/tax guard
* GitHub workflow-equivalent checks
* documentation
* ADRs

---

# 23. FINAL FULL-SUITE COMMAND PASS

Immediately before the final Stage 15 commit, rerun the canonical equivalents of:

```bash
dotnet build -c Release
dotnet test -c Release
```

plus:

* coverage command
* migration test
* architecture tests
* Stage 15 integration tests
* OpenAPI tests
* applicable GitHub Actions commands
* required guard panel
* seed verification if automated

This final run matters even if these commands passed earlier because TASK-15-04 may have changed shared Stage 15 infrastructure.

---

# 24. DO NOT CHEAT THE GREEN CHECKPOINT

Never make CI green by:

* deleting tests
* skipping tests
* weakening assertions without architectural justification
* disabling nullable warnings
* broadly suppressing analyzers
* excluding Stage 15 from coverage
* marking integration tests ignored
* removing migration Down tests
* bypassing authorization
* bypassing `CompanyLink`
* bypassing Stage 05 approval
* writing directly to Stage 10 tables
* directly constructing downstream rows that should go through existing commands
* replacing real integration tests with mocks
* changing GitHub workflow conditions so jobs stop running

Fix the implementation.

---

# 25. AUTONOMOUS FAILURE LOOP

Whenever a command fails:

1. read the full relevant error
2. determine root cause
3. inspect the relevant code
4. make the smallest correct fix
5. rerun the failing targeted command
6. when green, rerun the broader affected tests
7. continue

Do not abandon a task because the first implementation failed.

Do not repeatedly make speculative edits without rerunning the test that exposed the issue.

---

# 26. CONTEXT MANAGEMENT

Keep context focused.

Do not continuously stack the entire repository into context.

For each task load only:

* task
* authoritative references
* affected code
* analogous implementations
* failing tests/logs

Once TASK-15-01 is committed, summarize its established public seams and move to TASK-15-02 without carrying every implementation file in context.

Repeat for later tasks.

Repository state, tests, task files and commits are the durable memory.

---

# 27. COMPLETION RULE

Stage 15 may only be marked:

`DONE`

when every mandatory exit item is proven green.

If something genuinely cannot be completed because of an external/environmental blocker, do NOT pretend the stage is done.

Record:

```text
BLOCKED:
<exact item>

CAUSE:
<exact evidence>

COMPLETED:
<everything already proven green>

REMAINING:
<smallest precise remaining action>
```

Do not replace a real implementation failure with `BLOCKED`.

A blocker means something outside the code/repository prevents execution.

---

# 28. FINAL RESPONSE TO USER

When all work is complete, respond with a concise engineering summary containing:

### Tasks

* TASK-15-01 — status + commit
* TASK-15-02 — status + commit
* TASK-15-03 — status + commit
* TASK-15-04 — status + commit

### Implementation

Major components added.

### Tests

Exact test/build commands run and pass counts.

### Coverage

Actual Stage 15 Domain/Application percentage.

### CI

Applicable GitHub workflow jobs/checks reproduced and result.

### Database

Migration Up/Down result.

### Architecture

Guard/architecture test result.

### Git

Final branch and commit hashes.

### Remaining issues

Only real unresolved issues. If none:

`None.`

Do not fill the final response with implementation speculation.

The repository, green tests, evidence and commits are the result.

---

Begin now with **TASK-15-01**.

Inspect the repository state and authoritative files, implement it completely, prove its acceptance criteria, commit the green checkpoint, then continue autonomously through TASK-15-04 and final Stage 15 verification.
