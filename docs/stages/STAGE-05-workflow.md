# STAGE 05 — Workflow, Approvals, Notifications, Documents

**Status:** DONE · **Depends on:** 04b · **Reference reading:** `docs/DECISIONS.md` ADR-019
(the live decision), `docs/CONVENTIONS.md`, `docs/API_STANDARDS.md`, `docs/TESTING.md`,
`docs/DATA_MODEL.md`, `docs/SECURITY.md`, `docs/stages/STAGE-04b-licensing.md` (structural template
and the sibling-project pattern this stage follows)

## Objective

Build the approval, notification and document primitives once, centrally, before any business module
needs them (ADR-019). Ten later modules — procurement, HR, imports, quality, projects, finance and
any threshold-gated action — configure against this engine rather than each hand-rolling their own
approval chain, their own email footer and their own attachment table. `CLAUDE.md` §7 rule 13 makes
this a hard rule: no module implements its own approval logic, and an architecture test enforces it.

This stage does not build any business workflow itself — there is no purchase order or leave request
yet to approve. It builds the choke point (`IApprovalService`), the unified pending-approval inbox,
the notification dispatch abstraction with a working in-process/log implementation, and the document
attachment/versioning primitives that Stage 09's receipts and every later module's file attachments
will build on. Real QuestPDF rendering, real email/push providers and the Android inbox UI are
deliberately out of scope — they are Deferred items for the stages that own them, not blockers here.

## Note on Stage 04b's dependency

`docs/ROADMAP.md` marks Stage 05 as depending on 04b. As of this session, 04b's **code and tests are
complete and green** (524 passing at hand-off) and only its documentation debt is outstanding — see
`docs/PROGRESS.md` §5, items 1–2 (`docs/TESTING.md` §7 and `docs/LICENSING.md` §4 still speak the
superseded lockout language of ADR-027 rather than the live ADR-028). Nothing this stage needed was
inside that outstanding documentation; every mechanism Stage 05 depends on — `IEntitlementService`,
the pipeline's `ReadOnlyGuard` slot, the permission catalogue, the module manifest registry — is code
that was already built and green. This stage proceeded without waiting.

## Deliverables

**Domain (`workflow` module, `src/VumaRetail.Domain/Workflow/`)**
- `ApprovalPolicy` — data, not code: `module.entityType.action`, an optional `Money` threshold, the
  permission an approver must hold, how many approvals are required, and whether the requester may
  approve their own request. Absence of an active policy for a given key means "no gate" — a module
  that wants approval must configure it, which is what "later stages configure rather than build"
  means in practice.
- `ApprovalRequest` — one pending (or decided) gate, snapshotting the policy's rules at the moment it
  was raised so a later policy edit cannot retroactively change a request already in flight.
- `ApprovalDecisionEntry` — append-only, one row per decision, independent of the request's own
  mutable state so the full history survives whatever the request's current status says.
- `Notification` — one message to one recipient on one channel; in-app, email or push.
- `Document` and `DocumentVersion` — attachment/generation metadata and an append-only version
  history, separate from the bytes themselves (which live behind `IDocumentBlobStore`).

**New project: `VumaRetail.Workflow`** (ADR-054), on the `VumaRetail.Sync` / `VumaRetail.Licensing`
pattern (ADR-048/ADR-051): references `Application` and `Contracts` only — no EF, no HTTP, no
filesystem. Holds:
- `ApprovalEngine` — the **sole** implementation of `IApprovalService`. Evaluates a policy, creates or
  auto-approves a request, and records decisions with separation-of-duties and required-permission
  checks. This is the choke point rule 13 requires.
- The approval, notification and document commands, queries, validators and permissions.
- `LogNotificationChannel` for email and push — a working in-process implementation that logs what
  would have been sent (the `InProcessControlPlane` pattern from Stage 04b), and `InAppNotificationChannel`,
  which is a real, persisted, working channel because in-app notification is the one channel this
  product ships without a vendor integration.
- `PassthroughDocumentRenderer` — a trivial working `IDocumentRenderer` that stores whatever bytes or
  text it is given as a generated document version. Real QuestPDF template rendering is Deferred to
  the stage that first needs a real receipt or statement (Stage 09's `VumaRetail.Reporting`, per
  `CLAUDE.md` §6).

**Infrastructure**
- EF configurations and repositories for all six entities, in the new `workflow` schema.
- `FileSystemDocumentBlobStore` — a working `IDocumentBlobStore` on the `FileSystemBackupVault`
  pattern: content-addressed by SHA-256, written to a staging file and moved into place so a crash
  mid-write cannot leave a truncated object at a key a `DocumentVersion` row claims is complete.
- `AddVumaWorkflow(...)` DI registration, called after `AddVumaPersistence`.
- A reversible EF Core migration (`Workflow`), up → down → up verified.

**API (`VumaRetail.Web`, `VumaRetail.StoreServer`)**
- `/api/v1/workflow/approval-policies` — define and deactivate policies.
- `/api/v1/workflow/approvals` — the unified pending-approval inbox (every module, one list),
  approve/reject/cancel a request.
- `/api/v1/workflow/notifications` — a user's own notification list, unread count, mark read.
- `/api/v1/workflow/documents` — attach, generate, list versions, download a version.

**Permissions** (`workflow` module, registered in the RBAC catalogue): `workflow.policy.configure`,
`workflow.approval.view`, `workflow.approval.decide`, `workflow.document.view`,
`workflow.document.attach`.

## Business rules

- **No module implements its own approval logic.** Everything gates through `IApprovalService`.
  Enforced by an architecture test that asserts only `VumaRetail.Workflow` implements the interface,
  proven by a deliberate violation before being committed.
- **A gate exists only where a policy says it does.** No configured policy for a `module.entityType.action`
  key means the action is not gated — `EvaluateAsync` returns `AutoApproved` immediately. This is what
  lets a module call the choke point unconditionally on every threshold-gated action without knowing
  in advance whether an approval is actually required for this tenant.
- **A policy's rules are snapshotted onto the request that used them.** Changing a policy's threshold
  or required permission never changes what an already-raised request needs to clear.
- **Separation of duties is enforced in the engine, not left to the caller.** Unless a policy
  explicitly allows self-approval, the requester may never decide their own request. The engine also
  checks that a deciding user actually holds the policy's required permission in the relevant store —
  the same `IRoleRepository.ListEffectivePermissionsAsync` Stage 02 built for `/me/permissions` — so a
  generic `workflow.approval.decide` grant is not itself the authority to approve everything.
- **Any single rejection ends a multi-approver request.** A veto model: one holder of the required
  permission saying no is enough to stop the action, which is the conservative default for a
  separation-of-duties control.
- **Approval, notification and document commands are ordinary writes.** None of them claim an
  ADR-028 read-only exemption. A lapsed tenant cannot approve a purchase order any more than it can
  raise one — that is the commercial lever working as intended, not a gap in this stage.
- **In-app notification is real; email and push are honest placeholders.** A notification is always
  persisted and readable through the API regardless of channel. Where the channel is email or push,
  `LogNotificationChannel` logs what would have been sent rather than silently discarding it or
  pretending a vendor integration exists that does not.
- **Documents are content-addressed and versioned, never overwritten.** Attaching a new version never
  replaces an old one; `Document.CurrentVersionNumber` points at the latest, and every prior version
  stays retrievable — the same non-destructive posture the stock ledger and the audit trail both take.

## Tests / acceptance

- Domain: policy threshold arithmetic (below/at/above, and the no-threshold "always gate" case);
  request state machine (pending → approved once `MinApprovals` is reached, pending → rejected on the
  first rejection, cancel only while pending, decide-after-decided refused); self-approval refused
  unless the policy allows it; notification state transitions (sent, failed, read); document version
  numbering and that a document's `CurrentVersionNumber` always matches its latest version.
- `ApprovalEngine` unit tests (NSubstitute over the repository and `IRoleRepository` ports, no
  database): no policy configured → auto-approved with no request row created; below threshold →
  auto-approved; at/above threshold → pending; a decider lacking the required permission is refused;
  self-approval refused and permitted per policy; N-of-M approval counting; single rejection ends a
  2-of-3 request outright.
- Integration (real PostgreSQL via `ApiHarness`): define a policy → raise a request through
  `IApprovalService` from a throwaway test handler → decide it → assert the persisted state and the
  `ApprovalDecisionEntry` row; the unified inbox query returns pending requests irrespective of which
  "module" raised them; notification dispatch creates a readable in-app row and calls the log channel
  for email/push; document attach → new version → blob round-trips through `FileSystemDocumentBlobStore`
  with a matching SHA-256; migration up → down → up.
- API: happy path, `401`, `403`, `422`/`409` for policy conflicts, and that read-only refuses a decide
  command with `403 LICENCE_READ_ONLY` (no exemption claimed, per the business rules above).
- Architecture: only `VumaRetail.Workflow` implements `IApprovalService` — violated deliberately with
  a throwaway second implementation in `Infrastructure`, confirmed red, then reverted and confirmed
  green.

## Exit checklist

- [x] `IApprovalService` is the only path to gating an action on approval; architecture test in place
      and proven by deliberate violation — `WorkflowRulesTests`, both tests confirmed red with a
      throwaway second `IApprovalService` in `Infrastructure`, then green again after it was removed
- [x] Unified approval inbox aggregates pending requests regardless of originating module —
      `ApprovalWorkflowTests.The_unified_inbox_lists_pending_requests_regardless_of_which_module_raised_them`
- [x] Separation of duties (no self-approval, required-permission check) enforced in the engine and
      covered by tests — `ApprovalEngineTests` (NSubstitute, no database) and
      `ApprovalWorkflowTests.Self_approval_is_refused_over_http_with_a_422_rule_violation` /
      `..._is_permitted_over_http_when_the_policy_explicitly_allows_it` (real HTTP, real bearer identity)
- [x] Notification dispatch works end to end for in-app (persisted, readable) and email/push
      (log-channel, `docs/PROGRESS.md` §3 entry for the real integrations) — `NotificationDispatchTests`;
      the two Deferred entries are in `docs/PROGRESS.md` §3
- [x] Document attach, generate, version and retrieve work end to end against `FileSystemDocumentBlobStore`
      — `DocumentWorkflowTests`, including the SHA-256 round-trip and that a new version never
      overwrites the one before it
- [x] EF migration reversible, up → down → up verified — `dotnet ef database update` to head, to `0`,
      back to head against a real PostgreSQL database by hand, and `MigrationTests` asserts the same
      thing (plus the model-vs-migrations diff) on every run
- [x] Permissions registered in the RBAC catalogue; demo seed extended with one policy and one
      notification so the module is demonstrable — `DemoSeed.EnsureApprovalPolicyAsync` /
      `EnsureNotificationAsync`; `WorkflowPermissions` and `WorkflowModuleManifest` registered via
      `AddVumaWorkflow`
- [x] `dotnet build -c Release` — 0 warnings, 0 errors; `scripts/test.sh` fully green;
      Domain + Application new-code line coverage ≥ 80% — 600 passed, 0 failed, 0 skipped
      (313 unit, 28 architecture, 259 integration); Domain + Application Workflow line coverage 94.4%
      (336/356; Domain files 100%, `WorkflowPorts.cs` 82.3%)
- [x] `docs/DATA_MODEL.md` and `docs/SYNC_AND_BACKUP.md` updated with the `workflow` schema and its
      replication registry entries — `DATA_MODEL.md` §3 and new §4c; `SYNC_AND_BACKUP.md` §3
- [x] `docs/PROGRESS.md` updated, ADRs appended, committed — ADR-054, ADR-055; session log below
