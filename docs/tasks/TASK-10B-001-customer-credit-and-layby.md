# TASK-10B-001 — Customer credit accounts and lay-by

**Status:** NOT_STARTED · **Depends on:** Stages 07, 09, 10 (all built) · **Reference reading:**
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
- Sale completion mechanics (Stage 09 owns `ISaleCompletionService`; lay-by completion calls it,
  never reimplements it).
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
  (`Money`, snapshotted from terms at opening), `CompletedSaleId`, `CancelledAt`,
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
- `Commands/Accounts/`: `OpenCustomerAccountCommand(PartnerId, CreditLimit, TermsDays)`,
  `SetCreditLimitCommand(AccountId, NewLimit)` (must call `IApprovalService.EvaluateAsync`;
  refused without approval), `PlaceAccountHoldCommand(AccountId, Reason)` (approval-gated),
  `ReleaseAccountHoldCommand(AccountId)`, `RecordAccountPaymentCommand(AccountId, Amount,
  Channel, IdempotencyKey)` (posts an `ArReceipt` + allocation; idempotent on the key).
- `Commands/LayBy/`: `OpenLayByAgreementCommand(PartnerId, Lines, DepositAmount, TermMonths)`
  (resolves prices via `IPriceResolver`/`ITaxCalculator`/`IPackSizeResolver`, freezes
  `AgreedTotal`, creates `StockReservation.Hold` rows with `ReservationSource.LayBy`, raises
  `LayByDepositReceivedEvent` when a deposit is taken with the same command),
  `RecordLayByInstalmentCommand(AgreementId, Amount, Channel, IdempotencyKey)` (idempotent;
  sets `TakenOffline` when `ITenantContext` reports the terminal offline),
  `CompleteLayByAgreementCommand(AgreementId)` (requires fully paid and connectivity; builds the
  Stage 09 `Sale` from the agreement snapshots and calls `ISaleCompletionService.CompleteAsync`
  exactly once; consumes the reservations; raises `LayByCompletedEvent`),
  `CancelLayByAgreementCommand(AgreementId)` (refund = paid − snapshotted fee; releases
  reservations; raises `LayByCancelledEvent` with named amounts `Refund` and `Fee`).
- `Queries/`: `GetAccountStatementQuery(AccountId, From, To)` (invoice → part payment →
  credit note lines with running balance), `GetAccountAgeingQuery(AccountId, AsAt)` (buckets
  Current/30/60/90/120+ from due dates), `CheckCreditLimitQuery(AccountId, TenderAmount,
  QueuedOfflineTotal)` (available = limit − AR outstanding − queued offline; refuses when
  tender exceeds available or status is not Active).
- `Events/`: `LayByDepositReceivedEvent`, `LayByInstalmentReceivedEvent`,
  `LayByCompletedEvent`, `LayByCancelledEvent`, `AccountInterestRaisedEvent` — all implement
  `IFinancialEvent` with `Amounts` dictionaries only (`Principal`, `Fee`, `Refund`, `Interest`);
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
   when status ≠ Active. Unsync
...[truncated 6151 chars]