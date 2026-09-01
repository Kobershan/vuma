# IMPORT_PIPELINE.md — Excel, CSV and PDF ingestion

> Built in Stage 11. The module that turns "here is my spreadsheet" into master data, and — the part
> that makes the rest safe to use — turns it back again.

R5 is one line in `CLAUDE.md` §3: *"Ingest from Excel/CSV/PDF for suppliers, customers, inventory and
specials — with mapping, preview, validation and rollback."* It is the difference between a system
somebody can start using on a Monday and a system somebody has to type their whole business into
first. This document describes what was built for it.

The four words in R5 are the design, and they are load-bearing in this order:

- **Mapping** — the shop's file has the columns the shop's file has. Nobody is going to rewrite a
  supplier's price list into our column order, so the mapping is *data*, it is saved, and the same
  supplier's file next month maps itself with no human step.
- **Preview** — an import that writes first and shows you afterwards is not an import, it is an
  accident. Nothing touches a business table until somebody has seen the rows.
- **Validation** — per row, not per file. One bad barcode on line 400 must not cost you the other 399,
  and it must tell you it was line 400.
- **Rollback** — the promise that makes the other three safe. Also the hardest, because §7 rule 6 says
  stock is never a mutable column and rule 8 says nothing is hard-deleted, so a rollback cannot be a
  `DELETE`. See ADR-076.

---

## 1. The pipeline

```
Uploaded ─ parse ─→ Parsed ─ map ─→ Mapped ─ validate ─→ Validated ─ commit ─→ Committed
                                                                                   │
   └──────────────── discard ───────────────→ Discarded            rollback ───────┘
                                                                                   ↓
                                                                             RolledBack
```

`ImportBatch` is the aggregate root and the state machine, and the transitions are the safety model
rather than bookkeeping. Everything left of `commit` writes only inside the `imports` schema.

| Status | What it means | What has been written outside `imports` |
|---|---|---|
| `Uploaded` | Bytes stored and hashed, not yet read | Nothing |
| `Parsed` | Headers and rows read, mapping still needed | Nothing |
| `Mapped` | Every required target field is bound | Nothing |
| `Validated` | Every row has a verdict — this is the preview | Nothing |
| `Committed` | Every valid row applied, in one transaction | The batch's rows |
| `RolledBack` | Every committed row compensated | Compensations only |
| `Discarded` | Abandoned before commit | Nothing, ever |

**Upload, parse and auto-map are one command**, not three. They are one thing a person does, and a
batch that is uploaded but unparsed is a state nobody can act on.

---

## 2. Readers

One `IImportSourceReader` per `ImportSourceFormat`, selected by the format the upload declares. A
reader's whole job is "what does this file say" — it knows nothing about targets, mapping or
validation, which is what lets Stage 12's scheduled supplier feed and Stage 21b's Connect proposals
reuse it without inheriting an import batch.

### CSV — `CsvImportSourceReader`

Hand-written RFC 4180 (ADR-077), because the corner cases *are* the feature: quoted fields, embedded
commas, embedded newlines, doubled quotes, CRLF or LF, a UTF-8 BOM, ragged rows, and a configurable
delimiter so a semicolon-separated European export works. The delimiter is detected from the header
row when the caller does not state one.

### Excel — `ExcelImportSourceReader`

ClosedXML, the `CLAUDE.md` §4 locked choice. First worksheet by default, a named worksheet by option
(`IMPORTS_WORKSHEET_NOT_FOUND` when it is not there). Formulas are read as their **cached values** and
dates as dates rather than as serial numbers — a date read as `45292` is the kind of silent corruption
that only surfaces months later.

### PDF — `PdfImportSourceReader`

PdfPig words plus their bounding boxes, clustered into rows by baseline and into columns by
x-position. Machine-generated, aligned supplier price lists only — which is what a supplier price list
actually is.

**No OCR.** A *scanned* PDF needs Tesseract, a native library and a trained data file this build cannot
acquire. It is stubbed behind `IOcrTextExtractor` per `CLAUDE.md` §1 and recorded in `PROGRESS.md`
under "Deferred". A PDF with no text layer is refused as `IMPORTS_PDF_HAS_NO_TEXT_LAYER`, never
silently imported as zero rows. Adding OCR later is a registration, not a change to the reader.

---

## 3. Mapping

An `ImportColumnMapping` binds **one source column to one target field**, or supplies a **constant
default** for a column the file does not have at all — a supplier file with no `currency` column, for a
tenant that trades in one currency. The constant is why templates beat aliases: alias matching cannot
bind a column that is not there.

A batch is mapped by, in order:

1. **A saved template.** `ImportMappingTemplate` is keyed by target kind and a **source signature** — a
   hash of the normalised header row. A file whose headers match is mapped on upload with no human
   step at all. This is the mechanism that makes the second month's import free.
2. **Header aliases.** Every target field declares the header texts that mean it — a supplier writes
   `Item Code`, another writes `SKU`, another writes `Stock Code`, and all three mean `sku`. Matching
   is case-, space- and punctuation-insensitive (everything that is not a letter or digit is stripped
   from both sides).
3. **A person**, via `PUT /mapping`, when a required field is still bound to nothing. The batch stays
   `Parsed` rather than being half-mapped silently.

Aliases live **with the field** rather than in a lookup table elsewhere, so adding a target field and
teaching the matcher about it are the same edit.

**Re-mapping invalidates every verdict.** The rows were judged against the old bindings, and leaving
them valid would let a commit apply a preview nobody ever saw.

---

## 4. Validation

Three passes per row, in this order, and the order matters:

1. **Structural** — does this cell parse as the type its field declares? A cell becomes `Money`,
   `Quantity`, `DateOnly` or `bool` here, **once**, and is stored parsed on the row
   (`CONVENTIONS.md` §6). A target handler never re-parses a string; a preview and a commit that parse
   separately are a preview and a commit that can disagree.
2. **Within-file** — do two rows claim the same natural key? Decidable without touching the database,
   and doing it before the semantic pass is what makes a 4,000-row price list take four seconds
   instead of four minutes. The *second* occurrence is the error, and it names the row that had it
   first. Two rows for one record cannot both be right, and applying both would silently keep whichever
   came last.
3. **Semantic** — does this unit of measure exist, is this SKU already taken, does this location exist?
   Asked of the target handler, which is the only thing that knows.

A row that fails structurally is not asked semantically: the answers would be noise on top of an error
the person has to fix first anyway.

**Every error on a row is collected, not just the first**, so one round trip fixes the row.

### The three verdicts

A row comes out of validation as exactly one of:

| Verdict | Meaning | Counted in |
|---|---|---|
| `Valid` | Will create or update something | `ValidRows` |
| `Invalid` | Will not apply, and says why, by field | `InvalidRows` |
| `Skipped` | Already exists, and the strategy is `Skip` | `SkippedRows` |

**A duplicate is its own answer, not a kind of valid** (ADR-080). Counting it as valid tells somebody
their 4,000-row supplier sheet will make 4,000 changes when 3,900 of those partners already exist and
it will make a hundred — which is exactly the preview being dishonest that this module exists to
prevent.

The batch's six counters are recomputed from the rows on every transition rather than incremented as
rows change. A counter that is incremented is a counter that drifts, and this one is what a person
reads before deciding to commit.

---

## 5. Commit

The one command that writes business data.

- **A row is the unit of success.** A commit applies every valid row and skips every invalid one; it
  does not fail the batch because row 400 is bad. The counters are the report.
- **A commit is one transaction.** That is about *partial writes*, not about validity: if the database
  refuses the four-hundredth valid row, none of the first 399 is kept. Half an import is the one
  outcome a rollback cannot describe. The handler writes nothing itself and commits nothing — the
  pipeline's `IUnitOfWork` does, once.
- **A batch commits once.** The status is re-read under a row lock (`FindForUpdateAsync`) *inside* the
  transaction, so two concurrent callers serialise and the second finds it already committed. An
  unlocked read lets both see `Validated` and both apply their rows.
- **A repeated commit is a replay, not an error.** The batch id *is* the operation's idempotency key
  (`API_STANDARDS.md` §9): a person whose browser timed out on a forty-thousand-row commit will press
  the button again, and the honest answer is the counters, not a 422 reading as though it failed. Only
  `Committed` replays — a `RolledBack` batch is refused, because "commit" after somebody deliberately
  took the import back is a different intention.
- **The duplicate strategy is asked again at commit time**, not trusted from the preview. Between the
  two, a partner may have been created by hand. A row that has become a duplicate since is skipped,
  not applied and not fatal.

### Writing through the domain (ADR-079)

A target handler builds entities with the same `Create` factories the command handlers use and saves
them through the same repositories, so **every invariant that protects the API protects the import**.
An imported supplier is a Stage 06 `Partner`, not a second kind of supplier.

It does **not** call another command's handler (`CONVENTIONS.md` §4) — a handler that calls a handler
is how two transactions and two validation passes end up inside one command.

---

## 6. Rollback

Compensating, never destructive (ADR-076). What that means per outcome:

| The row | Compensated by |
|---|---|
| `Created` an entity | Soft delete — `deleted_at` + `deleted_by`, filtered out by the global query filter (§7 rule 8) |
| `Updated` an entity | Restored from its **before-image**, field by field |
| Posted a `Movement` | A new, opposite ledger entry carrying the same batch reference (§7 rule 6) |

### The before-image

`ImportRow.BeforeImage` is the entity's prior state as JSON, written by the handler that changed it and
read back by nobody else. Each handler stores **only the fields it can change** — storing the whole
entity would break the image the first time a later stage adds a field, and restoring a field the
handler never touched would undo somebody else's edit.

A row that updated something and carries no before-image is refused
(`IMPORTS_BEFORE_IMAGE_MISSING`) rather than silently skipped. A rollback that reported success having
changed nothing is the one outcome ADR-076 is written to prevent.

### Two passes, and the first one writes nothing

Every row is asked whether it **could** be compensated (`CanCompensateAsync`) before any of them is.
That is what makes the all-or-nothing promise real rather than aspirational: a single-pass rollback
that discovered an obstacle on row 900 would have already un-imported 899 rows, with no way back.

**A rollback that cannot be complete is refused whole**, naming the blocking rows
(`IMPORTS_ROLLBACK_BLOCKED`). A partial rollback would leave a sale pointing at a deleted item, which
is worse than no rollback at all.

### Rows are compensated in reverse file order

An item created on row 12 and priced on row 40 must lose its price before it loses its existence, or
row 40's compensation runs against something already soft-deleted.

### What counts as "in the way"

`IImportUsageProbe` is the only thing in this module that deliberately looks across module boundaries,
and it has to: "can this imported item be removed" is answered by the sales, POS, inventory and finance
schemas at once. It is safe because of what it may do — it **reads**, it returns a sentence rather than
a row, and it names no other module's types in its signature.

It returns a phrase rather than a boolean because the refusal has to tell somebody *what* is in the
way; "no" on its own leaves them with a refusal and no next step.

**An import's own stock movements are not usage.** A file that creates an item and opens its stock is
one document, and treating its own second half as a blocker would make every combined import
unrollbackable. The probe excludes ledger entries whose reference type is `Import`.

---

## 7. Targets

Five `IImportTargetHandler` implementations, one per `ImportTargetKind`. Each declares a **field
catalogue** — name, type, required, description, a well-formed example, and the header aliases it
matches — which is what `GET /api/v1/imports/targets` serves, what auto-mapping uses, and what the
eventual mapping screen builds itself from. A sixth target is a registration, not a UI change.

| Target | Writes | Natural key | Store? |
|---|---|---|---|
| `Suppliers` | `Partner` (Stage 06) | `code` | No |
| `Customers` | `Partner` (Stage 06) | `code` | No |
| `Items` | `Item` + `Barcode` (Stage 06) | `code` | No |
| `StockOnHand` | `StockLedgerEntry` via `IStockLedgerPoster` (Stage 08) | `sku` + `location` | **Yes** |
| `PriceListLines` | `PriceListLine` (Stage 10) | `priceList` + `sku` + `minimumQuantity` | No |

The natural key is **declared**, not inferred from which fields are required. The two coincide for
partners and items and come apart immediately for stock, whose quantity is required and is emphatically
not part of what identifies a row — inferring it would mean two people counting the same shelf
differently went unnoticed, which is the one thing a stocktake import must catch.

### Suppliers and customers

Two target kinds over one entity because the *files* differ, and the catalogue is what a screen builds
its mapping UI from. One catalogue describing both would have to make every field optional, which is
how a required field quietly stops being checked.

**The address is all-or-nothing.** A file carrying a street with no town produces a row error naming
what is missing, rather than a partner with half an address — which looks complete on a screen and
fails at the point somebody tries to deliver to it.

**A partner who is both stays both.** A customer who turns up in a supplier file becomes
`Customer | Supplier`, not a supplier who has stopped being a customer. Overwriting the type would
silently drop the AR side of somebody the shop still sells to.

### Stock on hand

**This target writes no column, and that is the whole design.** §7 rule 6 says stock levels are the
projection of an append-only ledger, so an import that "sets stock to 40" is a lie there is no way to
write. What it does instead is post the **difference** between what the books say and what the sheet
says, through `IStockLedgerPoster`, referenced back to the batch — so the ledger explains where every
unit came from and ADR-068's weighted-average cost is maintained by the same arithmetic that maintains
it for a receipt.

**The quantity column is a level, not a movement.** A stock sheet says "we have 40", not "add 40". A row
whose figure already matches the books is a no-op reported as a skip, not a zero-quantity ledger entry
nobody can interpret. Reading it as a delta is how re-uploading a corrected file doubles somebody's
stock.

**An increase needs a cost the first time.** Stock arriving into a balance that does not exist yet has
no average to be valued at, and inventing one would put a fictional number into cost of sales.

The duplicate strategy does not apply here: a stock level is not a record that can already exist. The
target's description says so rather than pretending to honour a setting it cannot act on.

### Price list lines — what "import specials" means

R5 says specials, and what a shop actually receives from a supplier is a list of products and prices
with a date range on the covering email. That is a **price list with an effective window**, which Stage
10 already models.

A `Promotion` is a reward *rule* — buy two get one, ten percent off a basket over R500, happy hour —
and a rule is not a row of values, so no file can carry one. Importing prices covers what a specials
sheet contains; the rule engine stays Stage 10's, configured by hand.

**The list must already exist.** An import that created price lists would let a typo in one cell open a
second `SPECIALS` list at a different priority, silently outranking the real one at the till.

**The price is in the list's currency, not the batch's.** Every other target denominates money in the
store's currency; a price list carries its own, and every line on it was authored in that currency.

---

## 8. Money, quantity and currency

- Parsed **once**, at validation, and stored parsed on the row. Handlers read the normalised
  dictionary, never the raw one.
- The currency is the **batch's**, resolved from the store then the tenant, and **never from the
  file**. A cell reading `USD 4.99` in a rand shop is a mistake in the spreadsheet; the parser strips
  the symbol and the amount is read in the currency the shop actually trades in. Letting a file choose
  would let one row's typo denominate a price list in a currency the till cannot tender.
- A tenant with no base currency cannot be imported into (`IMPORTS_NO_CURRENCY`). Guessing one would
  put a fabricated currency code onto every price the file carries.
- A decimal in the wrong locale (`1 234,56`) is **reported**, not silently read as 1.

---

## 9. Endpoints

`/api/v1/imports/...`, 13 operations, every one in `/openapi/v1.json`.

| Method | Route | Permission |
|---|---|---|
| `GET` | `/targets` | `imports.batch.view` |
| `POST` | `/batches` | `imports.batch.create` |
| `GET` | `/batches` | `imports.batch.view` |
| `GET` | `/batches/{id}` | `imports.batch.view` |
| `GET` | `/batches/{id}/preview` | `imports.batch.view` |
| `PUT` | `/batches/{id}/mapping` | `imports.batch.create` |
| `POST` | `/batches/{id}/validate` | `imports.batch.create` |
| `POST` | `/batches/{id}/commit` | `imports.batch.commit` *(high risk)* |
| `POST` | `/batches/{id}/rollback` | `imports.batch.rollback` *(high risk)* |
| `POST` | `/batches/{id}/discard` | `imports.batch.create` |
| `POST` | `/batches/{id}/save-template` | `imports.template.manage` |
| `GET` | `/templates` | `imports.batch.view` |
| `POST` | `/templates/{id}/deactivate` | `imports.template.manage` |

The preview is **paged** (`API_STANDARDS.md` §8) and filterable by row status. Nobody scrolls forty
thousand rows looking for the eleven bad ones; they ask for the invalid ones, fix those lines in their
spreadsheet, and upload again.

`GET /batches` deliberately does **not** load rows — a list of batches is a list of files, and eagerly
loading fifty thousand rows per batch to render a table of twenty-five filenames is the mistake that
list exists to avoid.

---

## 10. Error codes

| Code | When |
|---|---|
| `IMPORTS_EMPTY_UPLOAD` | The upload has no bytes |
| `IMPORTS_UPLOAD_TOO_LARGE` | Over `MaxUploadBytes` |
| `IMPORTS_TOO_MANY_ROWS` | Over `MaxRows` |
| `IMPORTS_SOURCE_UNREADABLE` | The bytes are not that format |
| `IMPORTS_SOURCE_HAS_NO_ROWS` | A header and nothing under it |
| `IMPORTS_DUPLICATE_HEADER` | Two columns share a header |
| `IMPORTS_WORKSHEET_NOT_FOUND` | The named sheet is not in the workbook |
| `IMPORTS_PDF_HAS_NO_TEXT_LAYER` | A scanned PDF — needs OCR, which is deferred |
| `IMPORTS_FILE_ALREADY_COMMITTED` | These exact bytes already committed, under any name |
| `IMPORTS_STORE_REQUIRED` | A store-scoped target with no store |
| `IMPORTS_NO_CURRENCY` | Tenant has no base currency and the store overrides none |
| `IMPORTS_UNKNOWN_SOURCE_COLUMN` | A mapping names a column the file lacks |
| `IMPORTS_UNKNOWN_TARGET_FIELD` | A mapping names a field the target lacks |
| `IMPORTS_TARGET_FIELD_BOUND_TWICE` | Two mappings, one field |
| `IMPORTS_REQUIRED_FIELDS_UNMAPPED` | A required field bound to nothing |
| `IMPORTS_MAPPING_BINDS_NOTHING` | A binding with neither column nor default |
| `IMPORTS_UNEXPECTED_BATCH_STATUS` | A transition the state machine does not allow |
| `IMPORTS_NOTHING_TO_COMMIT` | Every row is invalid |
| `IMPORTS_ROLLBACK_BLOCKED` | Something now depends on what was imported |
| `IMPORTS_BEFORE_IMAGE_MISSING` / `_UNREADABLE` | An updated row cannot be restored |
| `IMPORTS_TEMPLATE_CODE_TAKEN` | Template code already used in this tenant |

Per-row errors carry the **field** and the **row number**: `IMPORTS_FIELD_REQUIRED`,
`IMPORTS_FIELD_NOT_PARSEABLE`, `IMPORTS_FIELD_OUT_OF_RANGE`, `IMPORTS_UNKNOWN_REFERENCE`,
`IMPORTS_DUPLICATE`, `IMPORTS_DUPLICATE_WITHIN_FILE`, `IMPORTS_NOT_VALIDATED`.

---

## 11. Configuration

`Vuma:Imports`, bound to `ImportOptions`.

| Setting | Default | Why |
|---|---|---|
| `MaxUploadBytes` | 32 MB | A till does not have RAM for an arbitrary workbook |
| `MaxRows` | 50,000 | Refused at read time, before anything is stored |
| `MaxPreviewRows` | 200 | The preview page ceiling |
| `FileDirectory` | `import-files` | Where uploaded bytes live |
| `DeleteFileOnCommit` | `true` | The batch and its rows are the record; the file is only needed to re-parse |

The uploaded bytes are stored **outside** the batch row: a 40 MB workbook does not belong in a table a
list endpoint pages over, and the bytes have a different lifetime from the record of what was imported.

They are **not replicated**. The store server is where an import happens; the cloud gets the batch, the
rows and the outcome, which is what a head office asks about. R10 also applies — an uploaded customer
list is exactly the sort of document that has no business leaving the premises for vendor purposes.

---

## 12. Module

`imports`, `IsCore => false`. A shop that keys its own data in never buys this.

Permissions: `imports.batch.view`, `imports.batch.create`, `imports.batch.commit` (high risk),
`imports.batch.rollback` (high risk), `imports.template.manage`.

**No GL account, anywhere** (§7 rule 12). An opening-stock import raises a stock movement; Stage 07's
posting rules engine decides the accounts, exactly as Stage 08's adjustments do.

**No import screen.** Every deliverable is an endpoint, per R3, for the same Windows/WPF reason Stage 09
and Stage 10 stopped where they did. The field catalogue endpoint exists precisely so the eventual
screen can build its mapping UI without hard-coding anything.

---

## 13. Notes for later stages

- **Stage 12 (procurement)** wants a supplier price-list import on a schedule rather than by upload.
  The reader and target seams are the same; what it adds is a fetch. **Do not fork the pipeline.**
- **Stage 21b (Vuma Connect)** is this module's opposite: a supplier *publishes* prices rather than a
  retailer importing a file. `IImportTargetHandler` is the shape a Connect proposal should reuse — the
  retailer accepting a proposal is a commit, and it should be rollback-able for the same reasons.
- **`ImportRow.BeforeImage` is a general before-image mechanism** that only this module uses so far. If
  a later stage needs undo, extend it rather than inventing a second one.
- **OCR** arrives as a registration of `IOcrTextExtractor`, not as a change to `PdfImportSourceReader`.
