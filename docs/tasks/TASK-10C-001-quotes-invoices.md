# Task

## Status

COMPLETE (2026-09-07)

## Stage

Stage 10c — Quotes, Invoices & Sales Analytics

## Type

DOMAIN, APPLICATION, INFRASTRUCTURE, DATABASE, API, TESTING

## Objective

Implement quotes, invoices, and multi-company document splitting for Stage 10c. This is the core documentary layer: turning operational facts from Stages 10, 14, and 08c into legally binding documents with proper multi-company splitting (ADR-102) and pack size snapshots (ADR-112).

## Why

Without this task, there is no mechanism to produce a quote, generate an invoice from a fulfilled order, or split a multi-company order into distinct per-company invoices. The trading group's fiscal liability cannot be properly allocated.

## Scope

### Domain (`src/VumaRetail.Domain/`)

- **`Quote` entity** (`src/VumaRetail.Domain/Sales/Quotes/`): `Entity` + `IImmutableRecord` + `[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]`. Columns: `QuoteNumber` (ADR-065 sequence, series `QTE`), `CustomerId`, `Currency`, `Status` (Draft/Issued/Accepted/Rejected/Expired), `ValidUntil`, `Net/Tax/Gross` money totals, `ExpiryPolicy`, `GroupId` (nullable, for trading group links).
- **`QuoteLine` entity**: `SalesOrderLine`-like — item/variant, quantity, unit price, discount, tax, pack size snapshot (`PackSizeDescription`), `PriceListId` snapshot, `PromotionsSummary`. Immutable once line is created.
- **`Invoice` entity** (`src/VumaRetail.Domain/Sales/Invoices/`): `Entity` + `IImmutableRecord` + `[Replicated]`. Columns: `InvoiceNumber` (ADR-065, series `INV`), `SourceDocumentRef` (order/sale id), `CompanyId`, `GroupId` (nullable), `CustomerId`, `Currency`, `Status` (Draft/Posted/Cancelled), `Net/Tax/Gross`, `PostedAt`, `SourceDocumentType`.
- **`InvoiceLine` entity**: item/variant, quantity, unit price, discount, tax, `PackSizeDescription` snapshot, `PackSizeQuantity`, `PackSizeUnit`, `PriceListId` snapshot.
- **`SalesAnalytics` entity**: company-scoped aggregation with period, channel, category dimensions.
- **`QuoteStatus` enum**: Draft, Issued, Accepted, Rejected, Expired.
- **`InvoiceStatus` enum**: Draft, Posted, Cancelled.
- **`InvoiceSourceType` enum**: Order, Sale, Quote.
- **Domain exceptions**: `QuotesRuleException`, `InvoicesRuleException` with refusal reasons (expired quote, already-accepted quote, non-draft invoice, pack size not resolved).
- **Ports**: `IQuoteRepository`, `IInvoiceRepository`, `ISalesAnalyticsRepository`.
- **`IPriceResolver` reuse** for snapshotting prices into quotes.

### Application (`src/VumaRetail.Application/`)

- **Commands** (`src/VumaRetail.Application/Sales/Commands/Quotes/`):
  - `CreateQuoteCommand` — converts a basket to a non-binding quote, snapshots prices/tax/pack size
  - `IssueQuoteCommand` — transitions Draft→Issued
  - `AcceptQuoteCommand` — transitions Issued→Accepted, optionally creates an Order or Sale
  - `RejectQuoteCommand` — transitions Issued→Rejected
  - `ExpireQuoteCommand` — transitions to Expired
  - `ConvertQuoteToOrderCommand` — creates a SalesOrder from an accepted quote
  - `ConvertQuoteToSaleCommand` — creates a POS Sale from an accepted quote
  - `CreateInvoiceCommand` — generates invoice(s) from a fulfilled order/sale, handles multi-company splitting
  - `FinalizeInvoiceCommand` — posts an invoice (immutable)
  - `CancelInvoiceCommand` — cancels a draft invoice
- **Queries** (`src/VumaRetail.Application/Sales/Queries/`):
  - `GetQuoteQuery`, `ListQuotesQuery`
  - `GetInvoiceQuery`, `ListInvoicesQuery`
  - `GetSalesAnalyticsQuery`, `GetGroupAnalyticsQuery`
- **Services** (`src/VumaRetail.Application/Sales/Services/`):
  - `QuoteService` — quote lifecycle, price snapshotting via `IPriceResolver`, expiry checks
  - `InvoiceService` — invoice generation, multi-company splitting via `ISplitDocumentBuilder`, pack size resolution
  - `AnalyticsService` — company-scoped and group-level analytics
- **Validators** for all commands
- **Permissions**: `sales.quote.manage`, `sales.quote.view`, `sales.invoice.manage`, `sales.invoice.view`, `sales.analytics.view`
- **Module manifest**: `SalesModuleManifest` updated with new flags

### Infrastructure (`src/VumaRetail.Infrastructure/`)

- **EF Core configurations** (`src/VumaRetail.Infrastructure/Persistence/Configurations/Sales/`):
  - `QuoteConfiguration`, `QuoteLineConfiguration`, `InvoiceConfiguration`, `InvoiceLineConfiguration`, `SalesAnalyticsConfiguration`
  - `SalesConfigurations.cs` extended with new entity configurations
- **Repositories**: `QuoteRepository`, `InvoiceRepository`, `SalesAnalyticsRepository`
- **Migrations**: New migration `20260908..._Stage10c_Quotes_Invoices_Analytics` for company database + registry database
- **DI wiring**: Service collection extensions for new repositories/services

### API (`src/VumaRetail.Web/`)

- **`SalesEndpoints.cs` extended** with quote and invoice endpoints
- New `SalesContracts.cs` entries for DTOs
- New `SalesPermissions.cs` entries

### Seed

- Extend `DemoSeed` with sample quotes, invoices, and analytics data

## Out of Scope

- Sales analytics computation logic (TASK-10C-002)
- Stage 05 document delivery (PDF, email) — this stage references the port only
- WPF UI (deferred to Stage 08b + Desktop)
- Android admin app (Stage 30)

## Architecture

- **ADR-074**: Quotes/invoices/analytics are Stage 10c's responsibility.
- **ADR-102**: One order → N invoices. Splitting via `ISplitDocumentBuilder`. Each invoice is entirely inside one company's database.
- **ADR-112**: Pack size snapshot on every invoice line. Resolved from Stage 10 price lists at invoice generation time.
- **ADR-012**: Immutable posted documents. `IImmutableRecord` makes it structural.
- **ADR-075**: Tax computed and stored per line. No recomputation.
- **§7 rule 1**: Domain references nothing. Application references abstractions only.
- **§7 rule 2**: Every write goes through a command handler.
- **§7 rule 12**: No GL account names near this code.
- **§7 rule 20**: No handler touches two databases. `MultiCompanyGuardTests` enforce.
- **ADR-119**: Group analytics are stale by design. Never drive a commit.
- **ADR-103**: Reservations through 08c. `Available` not `OnHand`.

## Architectural Boundaries

- Domain references nothing (LayeringTests).
- Application references abstractions only.
- No cross-schema FK (CONVENTIONS.md §2): item/variant/location/company are bare ids.
- Every command carries `[CommandSideEffect]` (CommandClassificationTests); queries carry none.
- New entities carry `[Replicated]` (PersistenceRulesTests) + private EF ctor (ReplicationRulesTests).

## Dependencies

Stages 09, 10, 14, 08c on `main`. Stage 05 (workflow/approvals) for the document delivery port.

## Relevant Files

- `src/VumaRetail.Domain/Sales/` — existing sales domain (PriceList, Promotion, SalesReturn)
- `src/VumaRetail.Domain/Orders/SalesOrder.cs` — order aggregate precedent
- `src/VumaRetail.Domain/Primitives/Money.cs`, `Quantity.cs` — value objects
- `src/VumaRetail.Application/Sales/Commands/`, `Queries/`, `Permissions/` — existing command/query pattern
- `src/VumaRetail.Application/Abstractions/Sales/SalesPorts.cs` — `IPriceResolver`, `IPriceListRepository`
- `src/VumaRetail.Web/Sales/SalesEndpoints.cs` — endpoint mapping pattern
- `src/VumaRetail.Contracts/Sales/SalesContracts.cs` — DTO pattern
- `src/VumaRetail.Domain/Orders/OrderExceptions.cs` — exception pattern precedent
- `src/VumaRetail.Domain/Orders/SalesOrderLine.cs` — line entity pattern
- `src/VumaRetail.Infrastructure/Persistence/Configurations/Sales/SalesConfigurations.cs` — EF config pattern
- `tests/VumaRetail.UnitTests/Sales/` — test pattern
- `tests/VumaRetail.IntegrationTests/Sales/` — integration test pattern
- `docs/MULTI_COMPANY.md` §5 — invoice splitting contract
- `docs/stages/STAGE-08c-availability-sourcing.md` — `ISplitDocumentBuilder` interface

## Relevant Documentation

`docs/stages/STAGE-10c-quotes-invoices-analytics.md`, `docs/DECISIONS.md` ADR-074/102/112/012/075, `docs/MULTI_COMPANY.md`, `docs/DATA_MODEL.md` §4h/§4f/§2b, `docs/SYNC_AND_BACKUP.md` §3, `docs/TESTING.md`, `docs/CONVENTIONS.md` §2/§4/§5, `docs/EXECUTION_STANDARD.md`.

## Implementation Requirements

- **Price snapshotting**: When a quote is created, call `IPriceResolver.ResolveAsync()` and store the `PriceResolution` components (unit price, discount, tax, price list id, promotion summary) on each `QuoteLine`. These are immutable snapshots.
- **Pack size snapshotting**: When an invoice line is created, resolve the pack size from the item's UoM catalogue and store it as `PackSizeDescription` (e.g., "2 x Case of 12"). This is the item/variant's base UoM + pack configuration.
- **Multi-company splitting**: When `CreateInvoiceCommand` receives an order with `GroupDocumentRef`, call `ISplitDocumentBuilder.SplitAsync()` to produce one invoice per company. Each invoice is created in its respective company's database via `ICompanyDbContextFactory`. The `ISplitDocumentBuilder` interface already exists in the codebase (Stage 08c).
- **Immutability**: `Invoice.Post()` transitions to Posted and throws if already posted. `IImmutableRecord` prevents modification after posting.
- **Numbering**: Use ADR-065's `IDocumentNumberCounter` with series `QTE` and `INV`.
- **Analytics**: `SalesAnalytics` is a company-scoped read model. Group-level analytics use the registry's `analytics_projections` table with `AsAt` timestamps and stale contributor disclosure.
- **Company scoping**: Every query handler uses the ambient `ITenantContext` + `ICompanyContext`. A user can only see invoices for their permitted company.

## Data/Database Impact

New migration for company database: `sales.quotes`, `sales.quote_lines`, `sales.invoices`, `sales.invoice_lines`, `sales.analytics`.
New migration for registry database: `registry.analytics_projections`.
Both reversible (`Down` drops in reverse order).
Replicated: `StockReservation` = StoreToCloud/AppendOnly; `AvailableBalance` = NodeLocal.
New entities: `[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]` for quotes and invoices; `NodeLocal` for analytics.

## API Impact

All new endpoints in `/openapi/v1.json` with examples and error responses.

## Security

New permissions registered in catalogue. No hardcoded permission strings. Company scoping enforced at endpoint and handler level.

## Sync/Offline Impact

Quotes and invoices replicate `StoreToCloud`/`Bidirectional`. Analytics are `NodeLocal`. Group analytics fed by company outbox.

## Acceptance Criteria

1. Quote lifecycle: Draft → Issued → Accepted → Converted to Order, with prices snapshotting correctly against a mid-process Stage 10 price list change.
2. Multi-company invoice split: A Stage 14 order fulfilled across companies produces exactly two distinct invoices.
3. Pack size rendering: PDF and API response display snapshotted pack size descriptions.
4. Immutability: Attempting to mutate an issued invoice fails; corrections route through Stage 10 Credit Note flow.
5. Coverage ≥ 80% on Domain + Application.

## Tests Required

- Unit: quote state machine, invoice posting immutability, pack size snapshotting, multi-company split reconciliation, price snapshotting against a mid-process price list change.
- Integration (real PG): criteria 2 and 3, migration `Up`/`Down` round-trip, company scoping enforcement.
- Architecture: existing suites stay green.

## Edge Cases

- Quote created then price list changes before acceptance — quote prices remain snapshotted.
- Quote expired before acceptance — cannot be accepted.
- Order sourced across companies with one company having no stock — backorder line produces no invoice for that company.
- Pack size not resolvable for an item — invoice line created with "Each" default.
- Group analytics when a company is stale — returns with `AsAt` and stale flag, never blocks.

## Definition of Done

- [x] `dotnet build -c Release` zero warnings in Domain/Application, zero errors anywhere
- [x] `dotnet test` green; new code ≥ 80% line coverage on Domain + Application (92.1% union)
- [x] Migration generated, applied, reversible (`Down` tested on scratch DB)
- [x] Endpoints in `/openapi/v1.json` with examples + error responses (17 routes, asserted by `Every_sales_documents_operation_reaches_the_openapi_document`)
- [x] Permissions registered in catalogue; module manifest updated (`registry.analytics.view` added; sales manifest extended)
- [x] No handler touching two databases; `TradingGroupGuardTests` green (new entry-point row)
- [x] Task file Work Log complete; `docs/PROGRESS.md` + `docs/CURRENT.md` updated; committed

## Follow-up Findings

1. Leg-level auto-retry after a crash between legs needs a leg→document reference on `SagaLeg` (06d-owned schema). Posted legs are terminal; recovery today is the in-flight report plus credit notes.
2. Per-barcode pack definitions are a catalogue (Stage 06) extension; the snapshot column and `IPackSizeResolver` port already carry whatever it resolves.
3. `CostOfSale` on analytics rows is zero until Stage 08 valuation joins in; margin equals revenue until then, disclosed in code and docs.

## Work Log

- 2026-09-07: Repaired partial-work regressions (restored Stage 10 DI registrations the partial work had dropped; fixed endpoint contract mismatches; removed duplicate `Domain.Abstractions.Sales` ports).
- 2026-09-07: Domain completed with XML docs throughout: clock-injected `Issue/Accept/Post`, `Converted` status + `MarkConverted`, `CompanyId` assignment on create, corrected `InvoiceLine` validation codes, `[Replicated]` on lines. Removed the Domain `NoWarn(CS1591)`.
- Key design correction: `Quote`/`Invoice` are NOT `IImmutableRecord` — the persistence guard cannot tell Draft → Issued from vandalism (4 integration tests proved it). Status guards are the contract (ADR-141), following `Sale`/`SalesReturn`.
- 2026-09-07: Application completed: ADR-065 `QTE`/`INV` numbering, `AddQuoteLine` via price resolver + tax engine + pack resolver, `ConvertQuoteTo{Order,Sale}`, single-company `CreateInvoice` (Draft) + `Finalize` (posts + raises `sales.invoice.posted`) + `Cancel`, `GenerateInvoicesFromOrderCommand` delegating to `IInvoiceIssuingService` (ADR-142).
- 2026-09-07: Infrastructure completed: `InvoiceIssuingService` saga (intent + `SharedSourcing` links + per-company serialisable legs + replay), `PackSizeResolver` (UoM catalogue: base reads bare, packs read counted), real analytics rebuild, repositories loading aggregates with lines, financial event publishers with logging fallback.
- 2026-09-07: Endpoints rewritten onto the dispatcher with company binding, group permission gate, line-level responses with pack sizes; 17 routes proven in OpenAPI.
- Evidence: 1047 unit / 54 architecture / 472 integration green on real PostgreSQL; migration Up/Down round-tripped on scratch; seed proven on scratch (`QTE-000001`, `INV-000001`, `1 x Box of 12`).
