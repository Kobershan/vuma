# TASK-10B-002 — Stokvels and Stage 10b verification

**Status:** NOT_STARTED · **Depends on:** TASK-10B-001 (module skeleton, terms, ports, permissions
pattern) · **Reference reading:**
`docs/stages/STAGE-10b-accounts-layby-stokvel.md` §Deliverables (stokvels) + §Business rules 3–5 +
§Tests/acceptance (stokvel bullets) + §Exit checklist; ADR-055 (time-weighted benefits, append-only
ledger, no "set balance"); `docs/ARCHITECTURE.md` (one company context per handler; ports);
`src/VumaRetail.Application/Pos/SaleCompletionService.cs` (`ISaleCompletionService` — payout runs
as a normal sale); `src/VumaRetail.Domain/Inventory/StockReservation.cs` (`ReservationSource`);
`src/VumaRetail.Application/Abstractions/Workflow/WorkflowPorts.cs`
`IApprovalService.EvaluateAsync`, `INotificationDispatcher.SendAsync`; TASK-10B-001 (ports,
`CustomerAccountsPermissions`, manifest, event pattern).

## Objective

At the end of this task a store can run stokvels at scale: groups with roles, per-member balances
projected from an append-only contribution ledger, time-weighted benefit allocation matching a
hand-computed fixture to the cent, hamper baskets with reserved December stock, payouts that run
as normal sales, mid-cycle leaving on a stated pro-rata rule, member/group statements, and
treasurer/member visibility walls enforced in the query handlers. The task then closes Stage 10b:
5,000-transaction liability reconciliation, coverage, reversible migration executed on scratch
PostgreSQL, seed demo, OpenAPI presence, the review panel, and the handoff docs. Stokvel work
touches only the module skeleton TASK-10B-001 built; account/lay-by behaviour is extended, never
rewritten.

## What this task does not own

- Customer accounts and lay-by internals (TASK-10B-001 owns them; this task only reuses their
  ports, permissions pattern and posting-rule seeding).
- The sale engine, stock ledger, AR engine, approval engine, notification delivery (Stages
  09/08/07/05 own them; payouts call `ISaleCompletionService.CompleteAsync`, reservations call
  `StockReservation.Hold`, approvals call `IApprovalService.EvaluateAsync`).
- GL account selection (posting rules are tenant data; §7 rule 12).
- WPF/Android/storefront surfaces (later stages consume this task's REST API).

## Deliverables

### Domain — `src/VumaRetail.Domain/CustomerAccounts/` (same folder TASK-10B-001 opened)

- `StokvelGroup.cs` — aggregate: `Name`, `Type` (`StokvelType`: `Savings = 0, GroceryHamper = 1,
  Burial = 2, InvestmentBuying = 3`), `Constitution` (text: payout rules, visibility rule —
  member-sees-own-only unless the constitution says otherwise), `CycleStart`, `CycleEnd`,
  `StoreId`, `Status` (`StokvelStatus`: `Forming = 0, Active = 1, PayingOut = 2, Closed = 3`),
  `GroupNumber` (series `STK`, `IDocumentNumberSequence`). Group balance is projected, never
  stored: `Balance(IEnumerable<StokvelContribution> contributions, IEnumerable<StokvelPayout>
  payouts, IEnumerable<StokvelBenefitAllocation> benefits)`.
- `StokvelMember.cs` — `GroupId`, `PartnerId` (bare uuid, `PartnerType.Customer`),
  `Role` (`MemberRole`: `Member = 0, Chairperson = 1, Treasurer = 2, Secretary = 3`),
  `JoinedAt`, `LeftAt` (null while active), `ContributionObligation` (`Money` per cycle).
  Member balance is projected from that member's rows only.
- `StokvelContribution.cs` — append-only: `GroupId`, `MemberId`, `Amount` (`Money`),
  `ReceiptReference`, `PaidAt`, `Channel`, `TakenOffline`. No update or delete path exists.
- `StokvelBenefitAllocation.cs` — append-only: `GroupId`, `MemberId`, `Amount` (`Money`),
  `Basis` (text, e.g. `time-weighted 2026 cycle, weight 300000/650000`), `AllocatedAt`.
- `StokvelPayout.cs` — `GroupId`, `MemberId`, `Kind` (`StokvelPayoutKind`: `Goods = 0,
  Hamper = 1, Cash = 2, StoreCredit = 3`), `Amount` (`Money`), `SaleId` (set on goods/hamper
  settlement), `HamperBasketId` (hamper kind only), `Status` (`Requested = 0, Approved = 1,
  Settled = 2`), `RequestedAt`, `ApprovedAt`, `SettledAt`.
- `HamperBasket.cs` + `HamperBasketLine.cs` — `GroupId`, `Name`, `GroupPrice` (`Money`),
  season `ValidFrom`/`ValidTo`; lines: `ItemId`/`ItemVariantId` (exactly one),
  `QuantityValue`/`QuantityUom` scalars, `SubstitutionItemId`/`SubstitutionItemVariantId`
  (nullable, used when the line item is unavailable per the basket's substitution rule).
- `StokvelStatus.cs`, `StokvelPayoutKind.cs`, `MemberRole.cs`, `StokvelType.cs`,
  `StokvelExceptions.cs` (coded factories).
- Modify `src/VumaRetail.Domain/Inventory/StockReservation.cs`: add `StokvelHamper = 5` to
  `ReservationSource` with an XML doc comment. No other change to that file.

### Application — `src/VumaRetail.Application/CustomerAccounts/`

- `Commands/Stokvels/`: `CreateStokvelGroupCommand(Name, Type, Constitution, CycleStart,
  CycleEnd, StoreId)`, `AddStokvelMemberCommand(GroupId, PartnerId, Role, Obligation)`,
  `RecordContributionCommand(GroupId, MemberId, Amount, Channel, IdempotencyKey)` (idempotent;
  receipt carries the member running balance; sets `TakenOffline` when the terminal is
  offline), `AllocateBenefitsCommand(GroupId, BonusPool, AsAt)` (pure time-weighted math —
  formula in rule 9; writes one `StokvelBenefitAllocation` per member with the weight basis),
  `RequestPayoutCommand(GroupId, MemberId, Kind, Amount, HamperBasketId?)` (refuses when
  `Amount > member available`; goods/hamper payouts exceeding a **stale** balance require
  connectivity — rule 5 of the stage), `ApprovePayoutCommand(PayoutId)` (must call
  `IApprovalService.EvaluateAsync`; refused without approval), `SettlePayoutCommand(PayoutId)`
  (goods/hamper: builds the Stage 09 `Sale` from member balance + basket and calls
  `ISaleCompletionService.CompleteAsync` exactly once, stamps `SaleId`, consumes hamper
  reservations; cash/store-credit: raises the payout financial event),
  `RemoveMemberCommand(GroupId, MemberId)` (sets `LeftAt`; computes the rule-10 refund; member
  rows stay queryable forever).
- `Queries/`: `GetMemberStatementQuery(GroupId, MemberId, CallerPartnerId, CallerRole)` —
  refuses with a coded exception when a `Member` caller asks for another member's rows unless
  the constitution allows; `GetGroupStatementQuery(GroupId, CallerRole)` — treasurer, chair
  and secretary only.
- `Events/` (`IFinancialEvent`, named amounts only, no accounts):
  `StokvelContributionReceivedEvent` (`Principal`), `StokvelBenefitAllocatedEvent`
  (`Benefit`), `StokvelPayoutSettledEvent` (`Principal`, `BasketDiscount`).
- `Permissions/CustomerAccountsPermissions.cs` (extend TASK-10B-001's file, do not replace):
  `stokvel.manage` (`customeraccounts.stokvel.manage`, high-risk),
  `stokvel.view` (`customeraccounts.stokvel.view`).
- `Hosting/StokvelReminderHostedService.cs` (`BackgroundService`, daily 02:00 store-local:
  arrears reminders per member via `INotificationDispatcher`, hamper-season reservation top-up
  check in October).

### Infrastructure — `src/VumaRetail.Infrastructure/`

- `Persistence/Configurations/CustomerAccounts/` (extend TASK-10B-001's file): group/member/
  contribution/benefit/payout/basket configurations; contribution table has NO update path
  (no `Update` method on its repository); unique `(member_id, receipt_reference)` on
  contributions (offline replay idempotency at the storage layer); `HasMoney` for all amounts;
  scalar quantity columns on basket lines with the same `quantity_value > 0` constraint shape.
- `Persistence/Repositories/` (extend): `StokvelGroupRepository`, `StokvelContributionRepository`
  (append-only: `Add`, `Find`, list methods — no `Update`), `StokvelPayoutRepository`.
  Ports live in `Application/Abstractions/CustomerAccounts/CustomerAccountsPorts.cs` (extend).
- EF migration `Stage10b_Stokvels` (reversible `Down` executed on scratch DB).
- `StoreServer/DemoSeed.cs` (extend): one grocery stokvel (3 members, fixture contributions
  below), one hamper basket (December season), posting-rule rows for the three stokvel event
  types, all to liability accounts.
- `docs/SYNC_AND_BACKUP.md`: register the six stokvel tables as StoreToCloud entities.

### Contracts — `src/VumaRetail.Contracts/CustomerAccounts/` (extend)

`CreateStokvelRequest`, `StokvelResponse`, `AddMemberRequest`, `MemberResponse`,
`RecordContributionRequest`, `ContributionResponse`, `AllocateBenefitsRequest`,
`BenefitAllocationResponse`, `RequestPayoutRequest`, `PayoutResponse`,
`MemberStatementResponse`, `GroupStatementResponse`, `CreateHamperRequest`, `HamperResponse`.

### Web — `src/VumaRetail.Web/CustomerAccounts/` (extend)

`POST /api/v1/stokvels`, `POST /stokvels/{id}/members`, `POST /stokvels/{id}/contributions`,
`POST /stokvels/{id}/allocate-benefits`, `POST /stokvels/{id}/payouts`,
`POST /stokvels/payouts/{payoutId}/approve`, `POST /stokvels/payouts/{payoutId}/settle`,
`POST /stokvels/{id}/hampers`, `GET /stokvels/{id}/statement?memberId`, `GET
/stokvels/{id}/group-statement`, `POST /stokvels/{id}/members/{memberId}/remove` — all
permission-gated (`stokvel.manage` for writes, `stokvel.view` for reads) and present in
`/openapi/v1.json`.

## Business rules

1. Group balance is a projection: `Σ contributions − Σ settled payouts + Σ benefit
   allocations`, computed from member rows. There is no "set balance" operation on any
   entity; the repository offers no update path for contributions or allocations.
2. Time-weighted benefits (rule 9): distributable pool `B`, member weight `w(m) = Σ over
   that member's contributions of amount × whole days held (payout date minus contribution
   date, minimum 0)`, `share(m) = B × w(m) / Σw`, remainder dust to the highest-weight
   member by largest remainder. Benefits vest only for days the member was active
   (`JoinedAt..LeftAt ?? AsAt`).
3. Payout never exceeds available: `Amount ≤ paid_in + vested_benefits − settled_payouts`
   for that member, evaluated at settle time, not request time.
4. Goods/hamper payout is a normal sale: one `ISaleCompletionService.CompleteAsync` call —
   one stock issue set, one revenue recognition, correct tax and margin, goods traceable to
   the member. Cash/store-credit payout raises the payout event only.
5. Stale-balance gate: a payout evaluated against a balance older than the tenant's
   freshness threshold (seed default 15 minutes, `CustomerFinanceTerms` row TASK-10B-001
   created — add `StaleBalanceMinutes`, seed default 15) requires connectivity and refuses
   offline. Contributions never refuse offline.
6. Visibility wall: `Member` callers read only their own member rows unless the group's
   constitution text explicitly allows otherwise; treasurer/chair/secretary read the group.
   Enforced in the query handlers with a coded `StokvelVisibilityException`, covered by a
   permission test per role — declaring is not enforcing (mistake #5).
7. Hamper reservations reduce available stock from reservation day; substitution fires only
   when the line item's available is zero at settle time, and the substituted line is priced
   at the basket's group price, never re-resolved.
8. Mid-cycle leaving pro-rata (rule 10): `refund = paid_in − spent_share − fee` where
   `spent_share = group_committed_spend × paid_in / total_paid_in` and `fee` is the
   snapshotted admin fee. The member's rows remain for audit; `LeftAt` freezes vesting.
9. One company context per handler (§7 rule 20): a payout settling stock from another
   company's location is refused here — cross-company hamper sourcing is a saga owned by a
   later stage, not an inline second context.
10. Approval on every payout: `ApprovePayoutCommand` must call
    `IApprovalService.EvaluateAsync`; NSubstitute in the handler test proves the call
    happens (a gate with no call sites is mistake #7).

## Build list

- [ ] 1. Domain: `StokvelGroup.cs`, `StokvelMember.cs`, `MemberRole.cs`, `StokvelType.cs`,
      `StokvelStatus.cs` (create, join/leave, `Balance(...)` projection)
- [ ] 2. Domain: `StokvelContribution.cs`, `StokvelBenefitAllocation.cs` (append-only, no
      update path), `StokvelPayout.cs`, `StokvelPayoutKind.cs`, `HamperBasket.cs`,
      `HamperBasketLine.cs`, `StokvelExceptions.cs`
- [ ] 3. Domain: add `StokvelHamper = 5` to `ReservationSource`
      (`src/VumaRetail.Domain/Inventory/StockReservation.cs`); full solution builds
- [ ] 4. Application: ports in `CustomerAccountsPorts.cs` (extend);
      `Commands/Stokvels/` + validators + `AllocateBenefitsCommandHandler` (rule-9 math)
- [ ] 5. Application: payout request/approve/settle handlers (rules 3–5, 9–10),
      `RemoveMemberCommandHandler` (rule-8 math), statement queries (rule-6 wall)
- [ ] 6. Application: three `IFinancialEvent` records; extend permissions file; extend
      module manifest licence flag coverage (same `customer-accounts` flag)
- [ ] 7. Infrastructure: six EF configurations; three repositories (contribution repo has
      no `Update` — assert by construction); `add
...[truncated 5839 chars]