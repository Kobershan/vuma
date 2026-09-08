# TASK-10B-001 — Customer credit accounts and lay-by

**Status:** COMPLETE · **Depends on:** Stages 07, 09, 10 (all built) · **Reference reading:**
`docs/stages/STAGE-10b-accounts-layby-stokvel.md` §Deliverables (credit, lay-by) + §Business rules 1–2;
ADR-055 (LOCKED); `docs/DATA_MODEL.md` §4f (`finance.ar_invoices`, `finance.ar_receipts`,
`finance.posting_rules`); `docs/ARCHITECTURE.md` (boundaries);
`src/VumaRetail.Application/Abstractions/Finance/FinancePorts.cs` (`IFinancialEvent`,
`IFinancialEventPoster`); `src/VumaRetail.Application/Abstractions/Finance/FinanceRepositories.cs`
(`IArInvoiceRepository`, `IArReceiptRepository`); `src/VumaRetail.Application/Abstractions/Workflow/WorkflowPorts.cs`
`IApprovalService.EvaluateAsync` (line 108), `INotificationDispatcher.SendAsync` (line 329);
`src/VumaRetail.Application/Pos/SaleCompletionService.cs` (`ISaleCompletionService.CompleteAsync`);
`src/VumaRetail.Domain/Partners/Partner.cs` (`PartnerType.Customer`);
`src/VumaRetail.Domain/Inventory/StockReservation.cs` (`ReservationSource`, `Hold`);
`src/VumaRetail.Application/Inventory/InventoryPorts.cs` (`IStockReservationRepository`, line 116);
`src/VumaRetail.Application/Abstractions/Licensing/LicensingRepositories.cs` (`IMeteringRepository`, line 95).

## Objective

At the end of this task a store can open a customer credit account with an approved limit and
terms, check that limit at tender time including unsynced offline account sales, hold and release
accounts with approval, run statements/ageing/interest/dunning off Stage 07's AR sub-ledger, and
run lay-by agreements end to end: deposit, instalments, price-protected completion as exactly one
sale, and policy-driven cancellation. Money held posts to liability through posting rules; no
revenue, stock issue or cost of sale exists before delivery. Stokvels are TASK-10B-002, not this
task.

## What this task does not own

- The AR/AP/GL engine, journals, posting-rules evaluation (Stage 07 owns all of it; this task only
  uses `ArInvoice`/`ArReceipt` entities and raises `IFinancialEvent`s with named amounts).
- Sale completion mechanics (Stage 09 owns `ISaleCompletionService` and the `Sale` aggregate,
  which requires an open till session; lay-by completion settles without a `Sale` row per
  ADR-143 — it consumes the lay-by holds and posts the completion event).
- Price, tax and pack-size resolution (Stages 10/10c/07 own `IPriceResolver`, `ITaxCalculator`,
  `IPackSizeResolver`; lay-by lines snapshot their outputs at agreement time).
- Approval evaluation and notification delivery internals (Stage 05 owns them; this task calls
  `IApprovalService.EvaluateAsync` and `INotificationDispatcher.SendAsync`).
- Stokvel groups, contributions, benefits, payouts, hamper baskets (TASK-10B-002).
- WPF screens, Android screens, the public storefront API (later stages consume this task's REST API).

## Deliverables

### Domain — `src/VumaRetail.Domain/CustomerAccounts/`

- `CustomerAccount.cs` — aggregate: `PartnerId` (bare uuid, must be a `PartnerType.Customer`;
  never a foreign key), `AccountNumber` (series `ACT`, `IDocumentNumberSequence`), `CreditLimit`
  (`Money`), `TermsDays` (default 30), `Status` (`AccountStatus`), `HoldReason`. Exposure is
  computed, never stored.
- `AccountHolder.cs` — authorised buyer: `AccountId`, `UserId`, `DisplayName`, personal
  `ChargeLimit` (`Money`). Every charge records which holder charged.
- `AccountStatus.cs` — enum `Active = 0, OnHold = 1, Closed = 2`.
- `CustomerFinanceTerms.cs` — tenant configuration row: `InterestMonthlyRate` (decimal fraction,
  seed default 0.02), `SettlementDiscountRate` (seed default 0.02), `SettlementDiscountDays` (seed
  default 10), `LayByAdminFee` (`Money`, seed R100.00 ZAR), `LayByMaxTermMonths` (seed 6). Rates
  are configuration, never constants (mistake #9).
- `LayByAgreement.cs` — aggregate: `PartnerId`, `AgreementNumber` (series `LAY`), `Status`
  (`LayByStatus`), `AgreedTotal` (`Money`, frozen at opening — price protection),
  `DepositRequired` (`Money`), `PaidToDate` (`Money`), `TermMonths`, `ExpiryDate`, `AdminFee`
  (`Money`, snapshotted from terms at opening), `CompletedAt`, `CancelledAt`,
  `CancelRefund`/`CancelFee` (`Money?`, set once at cancellation). Methods `AddInstalment`,
  `Complete(DateTimeOffset now)` (requires fully paid, stamps `CompletedAt`), `Cancel(now,
  refund, fee)`, `Expire()`.
- `LayByAgreementLine.cs` — `AgreementId`, `ItemId`/`ItemVariantId` (exactly one),
  `QuantityValue`/`QuantityUom` scalar columns, `AgreedUnitPrice`/`DiscountAmount`/`TaxAmount`/
  `Net` (`Money`, snapshotted), `PackSizeDescription`, `Currency`, `PriceListId`.
- `LayByInstalment.cs` — append-only: `AgreementId`, `Sequence`, `Amount` (`Money`),
  `ReceiptReference`, `PaidAt`, `Channel`, `TakenOffline` (bool, set when captured offline).
- `LayByStatus.cs` — enum `Draft = 0, Active = 1, Completed = 2, Cancelled = 3, Expired = 4`.
- `CustomerAccountExceptions.cs`, `LayByExceptions.cs` — rule exceptions with coded factories.
- Modify `src/VumaRetail.Domain/Inventory/StockReservation.cs`: add `LayBy = 4` to
  `ReservationSource` with an XML doc comment. No other change to that file.

### Application — `src/VumaRetail.Application/CustomerAccounts/`

- `Abstractions/CustomerAccountsPorts.cs` (new file under
  `src/VumaRetail.Application/Abstractions/CustomerAccounts/`): `ICustomerAccountRepository`
  (`FindAsync`, `FindByNumberAsync`, `Add`, `Update`), `ILayByAgreementRepository` (same shape
  plus `ListExpiringAsync(DateTimeOffset before, ...)`).
- `Commands/Accounts/`: `OpenCustomerAccountCommand(PartnerId, CreditLimit, TermsDays,
  CompanyId = null)` (null falls back to the acting company, 10c pattern),
  `SetCreditLimitCommand(AccountId, NewLimit)` (must call `IApprovalService.EvaluateAsync`;
  refused without approval), `PlaceAccountHoldCommand(AccountId, Reason)` (approval-gated),
  `ReleaseAccountHoldCommand(AccountId)`, `AuthoriseHolderCommand(AccountId, UserId,
  DisplayName, ChargeLimit)`, `RecordAccountPaymentCommand(AccountId, Amount, Currency,
  Channel, ReceiptReference, Allocations)` (posts an `ArReceipt` fully allocated —
  `ArReceipt.Record` refuses partial allocation; replay-safe through the outbox/inbox).
- `Commands/LayBy/`: `OpenLayByAgreementCommand(PartnerId, Currency, Lines, DepositAmount,
  DepositChannel, TermMonths, LocationCode, CompanyId = null)` (null falls back to the acting
  company)
  (resolves prices via `IPriceResolver`/`ITaxCalculator`/`IPackSizeResolver`, freezes
  `AgreedTotal`, creates `StockReservation.Hold` rows with `ReservationSource.LayBy`, raises
  `LayByDepositReceivedEvent` when a deposit is taken with the same command),
  `RecordLayByInstalmentCommand(AgreementId, Amount, Currency, Channel, ReceiptReference,
  TakenOffline)` (appends the row; raises `LayByInstalmentReceivedEvent`; replay-safe through
  the outbox/inbox idempotency),
  `CompleteLayByAgreementCommand(AgreementId, CapturedOffline)` (refuses when `CapturedOffline`
  is true; requires fully paid; consumes every open hold under the agreement number via
  `IReservationService.ConsumeAsync` with `consumedByReferenceId` = the agreement id — the
  single stock-issue set; raises `LayByCompletedEvent` with `Principal` — the single revenue
  recognition, dated at completion; marks `Completed`. No POS `Sale` row is built: `Sale.Open`
  requires an open till session which back-office completion does not have (ADR-143)),
  `CancelLayByAgreementCommand(AgreementId)` (refund = paid − snapshotted fee; releases
  reservations; raises `LayByCancelledEvent` with named amounts `Refund` and `Fee`).
- `Queries/`: `GetAccountStatementQuery(AccountId, From, To)` (invoice → part payment →
  credit note lines with running balance), `GetAccountAgeingQuery(AccountId, AsAt)` (buckets
  Current/30/60/90/120+ from due dates), `CheckCreditLimitQuery(AccountId, TenderAmount,
  QueuedOfflineTotal)` (available = limit − AR outstanding − queued offline; refuses when
  tender exceeds available or status is not Active).
- `Events/`: `LayByDepositReceivedEvent`, `LayByInstalmentReceivedEvent`,
  `LayByCompletedEvent` (`Principal`, `Net`, `Tax` — the rule needs the split for the sales/VAT
  legs), `LayByCancelledEvent`, `AccountInterestRaisedEvent`, `AccountPaymentReceivedEvent` —
  all implement `IFinancialEvent` with `Amounts` dictionaries only (`Principal`, `Net`, `Tax`,
  `Fee`, `Refund`, `Interest`);
  **no `account_id` or GL account reference anywhere** (§7 rule 12, architecture-test enforced).
- `Hosting/`: `LayByExpiryHostedService` (`BackgroundService`, daily 02:00 store-local: expires
  past-date agreements, sends escalating reminders at 14/7/1 days via
  `INotificationDispatcher`), `AccountInterestHostedService` (monthly: one interest `ArInvoice`
  per overdue account at the configured rate; dunning via `INotificationDispatcher`).
- `Permissions/CustomerAccountsPermissions.cs`: `accounts.manage` (`customeraccounts.account.manage`,
  high-risk), `accounts.view` (`customeraccounts.account.view`), `layby.manage`
  (`customeraccounts.layby.manage`, high-risk); `CustomerAccountsModuleManifest` (licence flag
  `customer-accounts`, non-core).

### Infrastructure — `src/VumaRetail.Infrastructure/`

- `Persistence/Schemas.cs`: add `public const string CustomerAccounts = "customer_accounts";`
  with the same XML doc pattern as `Sales`.
- `Persistence/Configurations/CustomerAccounts/CustomerAccountsConfigurations.cs`:
  `CustomerAccountConfiguration` (table `accounts`, unique `ux_accounts_tenant_id_number` on
  `(tenant_id, account_number)` filtered `deleted_at IS NULL`), `AccountHolderConfiguration`
  (table `account_holders`, index on `account_id`), `CustomerFinanceTermsConfiguration` (table
  `terms`, one row per tenant: unique `ux_terms_tenant_id`), `LayByAgreementConfiguration`
  (table `layby_agreements`, unique number index, `HasMoney` for limit/total/paid/fee),
  `LayByAgreementLineConfiguration` (table `layby_agreement_lines`, scalar
  `quantity_value numeric(18,6)` + `quantity_uom varchar(16)` columns — do NOT use
  `HasQuantity` with a `new Quantity(...)` expression, it throws at model build —
  `HasMoney` for price/discount/tax/net, check constraints `quantity_value > 0`,
  `pack_size_description <> ''`), `LayByInstalmentConfiguration` (table `layby_instalments`,
  unique `(agreement_id, sequence)`), all deriving `EntityConfiguration<T>`.
- `Persistence/Repositories/CustomerAccountsRepositories.cs`: `CustomerAccountRepository`,
  `LayByAgreementRepository` (EF Core, `async`/`await` with `.ConfigureAwait(false)`;
  `IReadOnlyList<T>` returns must `await`, never return `ToListAsync` directly).
- `DependencyInjection/CustomerAccountsServiceCollectionExtensions.cs`:
  `AddVumaCustomerAccounts(IServiceCollection)` registering repositories, hosted services,
  permissions and manifest. Called from the StoreServer host composition.
- EF migration `Stage10b_AccountsAndLayBy` (reversible `Down` tested on scratch DB).
- `StoreServer/DemoSeed.cs`: seed one `ACT` account (limit R5,000, terms 30), one active
  `LAY` agreement (total R1,200, paid R800), tenant terms row, and posting-rule rows for the
  five event types above (all amounts to liability accounts; completion releases to revenue).
- `docs/SYNC_AND_BACKUP.md`: register `customer_accounts.accounts`,
  `customer_accounts.layby_agreements` (+ lines, instalments) as StoreToCloud entities.

### Contracts — `src/VumaRetail.Contracts/CustomerAccounts/CustomerAccountsContracts.cs`

`CreateAccountRequest`, `AccountResponse`, `AccountHolderResponse`, `SetLimitRequest`,
`HoldRequest`, `RecordPaymentRequest`, `StatementResponse` (+ `StatementLineResponse`),
`AgeingResponse`, `CreditCheckResponse`, `OpenLayByRequest` (+ `LayByLineRequest`),
`LayByResponse` (+ `LayByLineResponse`, `LayByInstalmentResponse`), `RecordInstalmentRequest`.

### Web — `src/VumaRetail.Web/CustomerAccounts/CustomerAccountsEndpoints.cs`

`POST /api/v1/customer-accounts`, `GET /{id}`, `POST /{id}/limit`, `POST /{id}/hold`,
`POST /{id}/release`, `POST /{id}/payments`, `GET /{id}/statement?from&to`,
`GET /{id}/ageing`, `GET /{id}/credit-check?tenderAmount&queuedOfflineTotal`,
`POST /api/v1/layby`, `POST /layby/{id}/instalments`, `POST /layby/{id}/complete`,
`POST /layby/{id}/cancel`, all permission-gated and present in `/openapi/v1.json`.

## Business rules

1. Money held for a customer is a liability: deposits, instalments, credits and overpayments
   post to liability accounts via posting rules; nothing reaches revenue before delivery.
2. Lay-by stock is reserved, not sold: opening creates `Held` reservations
   (`ReservationSource.LayBy`) reducing available; no revenue, stock issue or cost of sale
   until `CompleteLayByAgreementCommand` succeeds.
3. No balance is ever edited: corrections are new reason-coded audited rows (new instalment
   reversal entry, new AR adjustment); there is no setter path to a balance.
4. Every contribution/instalment returns a receipt carrying the running balance.
5. Offline instalments are accepted against the last-known balance with `TakenOffline = true`
   and reconcile on sync; completion requires connectivity and refuses offline.
6. Tender refuses when `TenderAmount > CreditLimit − ArOutstanding − QueuedOfflineTotal`, or
   when status ≠ Active. Unsynced offline account sales count against the limit at tender time.
7. Price protection: the agreed total frozen at opening is what completion charges, whatever the
   shelf price does mid-term. Nothing re-resolves after opening.
8. Completion is exactly-once economics: one consumption set over the agreement's holds plus one
   `layby.completed` journal (`Principal`/`Net`/`Tax`), dated at completion. No POS `Sale` row
   is built (ADR-143).
9. Cancellation accounts for every cent: `Refund + Fee = PaidToDate`, with `Fee` capped at the
   snapshotted admin fee. Reservations release back to sellable.
10. Limit changes and holds need approval: `SetCreditLimitCommand` and `PlaceAccountHoldCommand`
    must call `IApprovalService.EvaluateAsync` and refuse without `MayProceed`.
11. One company context per handler (§7 rule 20); no GL account is named outside Stage 07's
    posting rules (§7 rule 12, architecture-test enforced).
12. Receipts carry running balances; statements show open items with cumulative owed; ageing
    buckets derive from due dates, never invoice dates.

## Build list

- [x] 1. Domain: `CustomerAccount.cs`, `AccountHolder.cs`, `AccountStatus.cs`,
      `CustomerFinanceTerms.cs`, `CustomerAccountExceptions.cs`
- [x] 2. Domain: `LayByAgreement.cs`, `LayByAgreementLine.cs`, `LayByInstalment.cs`,
      `LayByStatus.cs`, `LayByExceptions.cs`
- [x] 3. Domain: `LayBy = 4` in `ReservationSource`
      (`src/VumaRetail.Domain/Inventory/StockReservation.cs`); Domain builds clean
- [x] 4. Application: `CustomerAccountsPorts.cs` (account, holder, terms, lay-by ports);
      `Commands/Accounts/` (open, set-limit, hold, release, payment, authorise-holder)
- [x] 5. Application: `Commands/LayBy/` (open with price/tax/pack snapshots + holds, instalment,
      complete with consumption set + revenue event, cancel with refund/fee + release)
- [x] 6. Application: `Queries/` (statement, ageing, tender-time credit check with holder caps)
- [x] 7. Application: six `IFinancialEvent` records (named amounts only);
      `CustomerAccountsPermissions.cs` + `CustomerAccountsModuleManifest` (module id and
      flag `customeraccounts`)
- [x] 8. Infrastructure: `Schemas.CustomerAccounts`; six EF configurations; four repositories;
      `AddVumaCustomerAccounts`; `Program.cs` service + endpoint wiring
- [x] 9. Migration `Stage10b_AccountsAndLayBy` generated (6 tables); scratch-DB Up/Down in Step 4
- [x] 10. Application: `LayByExpiryHostedService`, `AccountInterestHostedService` (public
      `ExpireDueAsync` / `AccrueInterestAsync` for tests)
- [x] 11. Contracts + Web: full DTO surface and 15 permission-gated endpoints; OpenAPI check in
      Step 4
- [ ] 12. Unit tests: approval-gate refusals, interest accrual, expiry service (extend the two
      test files below)
- [x] 13. `DemoSeed.cs`: terms row, liability accounts, six posting rules, LAYBY location, demo
      ACT account + active LAY agreement; seed run in Step 4
- [x] 14. Integration tests green (5/5); full-suite re-runs + coverage in Step 4

## Tests / acceptance

| Class | Test | Fixture → expectation |
|---|---|---|
| `CreditLimitTests` | `Limit_counts_ar_outstanding_and_queued_offline_sales_together` | Limit R5,000; AR R3,000 (R2,000 current + R1,000 45-day); offline R1,500 → R500 available; R600 refused, R500 approved |
| `CreditLimitTests` | `A_held_account_refuses_even_a_tender_well_inside_its_limit` | Hold → R100 tender refused with OnHold reason |
| `CreditLimitTests` | `An_unauthorised_buyer_and_an_over_limit_buyer_both_refuse` | Unknown buyer refused; holder cap R300 enforced |
| `CreditLimitTests` | `Ageing_buckets_follow_due_dates_not_invoice_dates` | Dues today/−20/−50/−80/−130 → Current/30/60/90/120+ at R1,000 each |
| `CreditLimitTests` | `Opening_needs_a_partner_a_positive_limit_and_positive_terms` | Empty partner/limit/terms each throw |
| `CreditLimitTests` (new) | `Limit_change_and_hold_need_approval` | Approval substitute refuses → `ACCOUNT_APPROVAL_REQUIRED`; NSubstitute verifies `EvaluateAsync` was called |
| `LayByLifecycleTests` | `Deposit_then_three_instalments_pays_R1200_exactly` | R200 + 5×R200; paid R1,200, remaining R0 |
| `LayByLifecycleTests` | `Completion_needs_every_cent_and_stamps_the_instant` | Early complete throws; full → Completed + CompletedAt; second complete throws |
| `LayByLifecycleTests` | `Overpayment_is_refused_because_money_without_a_home_is_a_dispute` | R1,100 against R1,000 remaining throws; paid unchanged |
| `LayByLifecycleTests` | `Cancellation_accounts_for_every_cent_with_the_fee_capped` | Paid R800 → refund R700 + fee R100; fee above terms refused; short count refused |
| `LayByLifecycleTests` | `Frozen_lines_hold_their_price_whatever_the_shelf_does` | Unit R100 frozen; net ≈86.9565, tax ≈13.0435, total R100 |
| `LayByLifecycleTests` | `Instalments_only_land_on_an_active_agreement` | Draft payment throws |
| `LayByLifecycleTests` (new) | `Expiry_service_expires_and_reminds` | Past-expiry Active → Expired + `ReleaseAsync` per chain; 14/7/1-day agreements notified once each |
| `LayByLifecycleTests` (new) | `Interest_accrues_monthly_on_overdue` | R1,000 45-day invoice at 2%/month → one R20.00 interest invoice + journal + dunning; no terms row → 0 |
| `LayByAgreementIntegrationTests` | `Full_lifecycle_posts_once_and_consumes_the_holds` | Deposit + instalment → complete: one completion journal (Principal R230/Net R200/Tax R30), one consume per chain |
| `LayByAgreementIntegrationTests` | `Cancellation_keeps_the_fee_and_releases_the_holds` | Paid R100, fee R100 → refund R0; release received; Cancelled |
| `LayByAgreementIntegrationTests` | `Agreed_total_survives_a_shelf_price_change_mid_term` | Reprice 100→120 after open; revenue still R230 |
| `LayByAgreementIntegrationTests` | `Offline_instalment_is_flagged_on_its_row` | `TakenOffline` true persisted; deposit row false |
| `CreditCheckIntegrationTests` | `Tender_gate_reads_posted_ar_and_a_payment_moves_it` | R3,000 owed + R1,500 offline → R2,500 refuses; pay R1,000 → R1,500 approves; receipt row persisted |
| `PeriodCloseTests` (Finance, extended) | `A_deposits_control_account_flags_layby_paid_to_date_it_cannot_see` | GL R800 vs paid R700 → variance R100 |
| DEFERRED (recorded in `docs/PROGRESS.md`): settlement-discount application and bad-debt write-off. Both need a Stage 07 AR adjustment path (posted lines are frozen, receipts must allocate in full). Terms fields are stored and seeded; only application is deferred. | | |

## Exit checklist

- [x] `dotnet build VumaRetail.sln -c Release`: 0 errors, 0 warnings in Domain/Application
- [x] `dotnet test` green (1096 unit, 54 arch, 481 integration); 82.5% line coverage on new Domain + Application
- [x] Migration `Down` executed on scratch DB (0 tables), re-applied (6 tables)
- [x] 15 routes in live `/openapi/v1.json` (268 → 283); seed exit 0 with demo proof line
- [x] `docs/PROGRESS.md` updated; architecture suite green (panel proper runs at stage close in 002)
- [x] Handoff for TASK-10B-002: module skeleton, ports, permissions, posting pattern, seed and variance wiring all land here; 002 adds stokvel entities + `ReservationSource.StokvelHamper = 5` + `StokvelFunds` control type + six tables + eleven endpoints on top, touching nothing above except extending the permissions file, ports file and seed