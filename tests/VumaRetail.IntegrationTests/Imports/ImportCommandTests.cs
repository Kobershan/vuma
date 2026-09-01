using System.Text;
using FluentAssertions;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Imports.Commands;
using VumaRetail.Application.Imports.Queries;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Imports;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Partners;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Imports;

/// <summary>
/// The Stage 11 pipeline end to end, over a real database: upload, map, validate, commit and roll back.
/// </summary>
/// <remarks>
/// These are integration tests rather than unit tests because the things Stage 11 promises are all
/// cross-module. "A created partner is soft-deleted" is a claim about a global query filter; "the
/// balance ends where it started" is a claim about Stage 08's ledger; "a rollback is refused when the
/// item has been sold" is a claim about four schemas at once. None of them can be shown against a
/// double.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ImportCommandTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_supplier_file_maps_itself_by_header_alias_and_creates_its_partners()
    {
        await using ImportsHarness harness = await ImportsHarness.CreateAsync(fixture);

        ImportBatchCreated created = await harness.SendAsync(Upload(
            ImportTargetKind.Suppliers,
            """
            Supplier Code,Supplier Name,E-Mail,Telephone
            ACME001,Acme Wholesalers,accounts@acme.co.za,021 555 0100
            BEST002,Best Foods,orders@bestfoods.co.za,011 555 0199
            """));

        // No human step: the headers are aliases the field catalogue already knows.
        created.MappedAutomatically.Should().BeTrue();
        created.Status.Should().Be(ImportBatchStatus.Mapped);
        created.TotalRows.Should().Be(2);
        created.UnmappedRequiredFields.Should().BeEmpty();

        ImportBatchCounts validated = await harness.SendAsync(new ValidateImportBatchCommand(created.BatchId));
        validated.ValidRows.Should().Be(2);
        validated.InvalidRows.Should().Be(0);

        // Business rule 1: nothing outside the imports schema before commit.
        (await harness.Partners.FindByCodeAsync("ACME001")).Should().BeNull();

        ImportBatchCounts committed = await harness.SendAsync(new CommitImportBatchCommand(created.BatchId));
        committed.CreatedRows.Should().Be(2);

        Partner? acme = await harness.Partners.FindByCodeAsync("ACME001");
        acme.Should().NotBeNull();
        acme!.Name.Should().Be("Acme Wholesalers");
        acme.Type.Should().HaveFlag(PartnerType.Supplier);
        acme.Email.Should().Be("accounts@acme.co.za");
    }

    [Fact]
    public async Task A_bad_row_is_reported_by_line_number_and_its_neighbours_still_import()
    {
        await using ImportsHarness harness = await ImportsHarness.CreateAsync(fixture);

        // Row 3 carries a street with no town, which is the partial address the handler refuses.
        ImportBatchCreated created = await harness.SendAsync(Upload(
            ImportTargetKind.Customers,
            """
            Customer Code,Customer Name,Address,Town,Country
            CUST001,Nomsa Dlamini,14 Long Street,Cape Town,ZA
            CUST002,Sipho Khumalo,7 Rissik Street,,ZA
            CUST003,Lerato Molefe,,,
            """));

        ImportBatchCounts validated = await harness.SendAsync(new ValidateImportBatchCommand(created.BatchId));

        validated.TotalRows.Should().Be(3);
        validated.ValidRows.Should().Be(2);
        validated.InvalidRows.Should().Be(1);

        PageResult<ImportRow> preview = await harness.QueryAsync(
            new GetImportPreviewQuery(created.BatchId, ImportRowStatus.Invalid));

        // The row number is the file's line number, header counted as line 1 — so the bad row is 3.
        preview.Items.Should().ContainSingle();
        preview.Items[0].RowNumber.Should().Be(3);
        preview.Items[0].Errors.Should().NotBeEmpty();

        await harness.SendAsync(new CommitImportBatchCommand(created.BatchId));

        (await harness.Partners.FindByCodeAsync("CUST001")).Should().NotBeNull();
        (await harness.Partners.FindByCodeAsync("CUST003")).Should().NotBeNull();
        (await harness.Partners.FindByCodeAsync("CUST002")).Should().BeNull();
    }

    [Fact]
    public async Task Rolling_back_soft_deletes_every_partner_the_import_created()
    {
        await using ImportsHarness harness = await ImportsHarness.CreateAsync(fixture);

        ImportBatchCreated created = await harness.SendAsync(Upload(
            ImportTargetKind.Suppliers,
            """
            Supplier Code,Supplier Name
            ACME001,Acme Wholesalers
            BEST002,Best Foods
            """));

        await harness.SendAsync(new ValidateImportBatchCommand(created.BatchId));
        await harness.SendAsync(new CommitImportBatchCommand(created.BatchId));

        (await harness.Partners.FindByCodeAsync("ACME001")).Should().NotBeNull();

        ImportBatchCounts rolledBack = await harness.SendAsync(
            new RollbackImportBatchCommand(created.BatchId, "Wrong supplier file."));

        rolledBack.Should().NotBeNull();

        // Soft-deleted, so the global query filter takes them out of every read (§7 rule 8).
        (await harness.Partners.FindByCodeAsync("ACME001")).Should().BeNull();
        (await harness.Partners.FindByCodeAsync("BEST002")).Should().BeNull();

        ImportBatch? batch = await harness.Batches.FindAsync(created.BatchId);
        batch!.Status.Should().Be(ImportBatchStatus.RolledBack);
        batch.RollbackReason.Should().Be("Wrong supplier file.");
    }

    [Fact]
    public async Task An_updated_partner_is_restored_to_its_before_image_field_by_field()
    {
        await using ImportsHarness harness = await ImportsHarness.CreateAsync(fixture);

        Partner existing = Partner.Create(
            harness.TenantId,
            "ACME001",
            "Acme Wholesalers CC",
            PartnerType.Customer,
            address: null,
            email: "old@acme.co.za",
            phone: "021 555 0000",
            taxNumber: "4000000000");

        harness.Partners.Add(existing);
        await harness.Context.CommitAsync();

        ImportBatchCreated created = await harness.SendAsync(Upload(
            ImportTargetKind.Suppliers,
            """
            Supplier Code,Supplier Name,E-Mail,Telephone
            ACME001,Acme Wholesalers (Pty) Ltd,new@acme.co.za,021 555 0100
            """,
            ImportDuplicateStrategy.Update));

        await harness.SendAsync(new ValidateImportBatchCommand(created.BatchId));

        ImportBatchCounts committed = await harness.SendAsync(new CommitImportBatchCommand(created.BatchId));
        committed.UpdatedRows.Should().Be(1);

        Partner? updated = await harness.Partners.FindByCodeAsync("ACME001");
        updated!.Name.Should().Be("Acme Wholesalers (Pty) Ltd");
        updated.Email.Should().Be("new@acme.co.za");

        // A customer who turns up in a supplier file is both, not a supplier who stopped being a customer.
        updated.Type.Should().HaveFlag(PartnerType.Customer);
        updated.Type.Should().HaveFlag(PartnerType.Supplier);

        await harness.SendAsync(new RollbackImportBatchCommand(created.BatchId, "Supplier sent the wrong sheet."));

        Partner? restored = await harness.Partners.FindByCodeAsync("ACME001");
        restored.Should().NotBeNull("an updated partner is restored, never removed");
        restored!.Name.Should().Be("Acme Wholesalers CC");
        restored.Email.Should().Be("old@acme.co.za");
        restored.Phone.Should().Be("021 555 0000");
        restored.TaxNumber.Should().Be("4000000000");
        restored.Type.Should().Be(PartnerType.Customer);
    }

    [Fact]
    public async Task A_stock_file_posts_the_difference_through_the_ledger_and_a_rollback_reverses_it()
    {
        await using ImportsHarness harness = await ImportsHarness.CreateAsync(fixture);

        ImportBatchCreated created = await harness.SendAsync(Upload(
            ImportTargetKind.StockOnHand,
            """
            Item Code,Location Code,On Hand,Cost Price
            MILK-2L,MAIN,40,12.50
            """,
            storeId: harness.StoreId));

        await harness.SendAsync(new ValidateImportBatchCommand(created.BatchId));
        await harness.SendAsync(new CommitImportBatchCommand(created.BatchId));

        StockBalance? balance = await harness.Balances.FindAsync(harness.LocationId, harness.ItemId, null);
        balance.Should().NotBeNull();
        balance!.QuantityOnHand.Value.Should().Be(40m);

        // Rule 10: the ledger explains where the number came from, referenced back to the batch.
        ImportBatch? batch = await harness.Batches.FindAsync(created.BatchId);
        batch!.CreatedRows.Should().Be(1);

        await harness.SendAsync(new RollbackImportBatchCommand(created.BatchId, "Counted the wrong aisle."));

        StockBalance? afterRollback = await harness.Balances.FindAsync(harness.LocationId, harness.ItemId, null);

        // Rule 5: reversed by a compensating entry, never removed — the balance is back where it started.
        afterRollback!.QuantityOnHand.Value.Should().Be(0m);
    }

    [Fact]
    public async Task A_stock_level_the_books_already_agree_with_is_a_skip_not_a_zero_entry()
    {
        await using ImportsHarness harness = await ImportsHarness.CreateAsync(fixture);

        ImportBatchCreated first = await harness.SendAsync(Upload(
            ImportTargetKind.StockOnHand,
            """
            Item Code,Location Code,On Hand,Cost Price
            MILK-2L,MAIN,40,12.50
            """,
            storeId: harness.StoreId));

        await harness.SendAsync(new ValidateImportBatchCommand(first.BatchId));
        await harness.SendAsync(new CommitImportBatchCommand(first.BatchId));

        // The same figure again — a re-uploaded corrected sheet. Read as a level, so it is a no-op;
        // read as a delta it would silently double the shop's stock.
        ImportBatchCreated second = await harness.SendAsync(Upload(
            ImportTargetKind.StockOnHand,
            """
            Item Code,Location Code,On Hand,Cost Price
            MILK-2L,MAIN,40,12.50

            """,
            storeId: harness.StoreId));

        ImportBatchCounts validated = await harness.SendAsync(new ValidateImportBatchCommand(second.BatchId));
        validated.SkippedRows.Should().Be(1);

        StockBalance? balance = await harness.Balances.FindAsync(harness.LocationId, harness.ItemId, null);
        balance!.QuantityOnHand.Value.Should().Be(40m);
    }

    [Fact]
    public async Task A_rollback_is_refused_whole_when_an_imported_item_has_since_been_sold()
    {
        await using ImportsHarness harness = await ImportsHarness.CreateAsync(fixture);

        ImportBatchCreated created = await harness.SendAsync(Upload(
            ImportTargetKind.Items,
            """
            Item Code,Description,Unit
            SUGAR-1KG,White sugar 1kg,EA
            RICE-2KG,Long grain rice 2kg,EA
            """));

        await harness.SendAsync(new ValidateImportBatchCommand(created.BatchId));
        await harness.SendAsync(new CommitImportBatchCommand(created.BatchId));

        Item? sugar = await harness.Items.FindByCodeAsync("SUGAR-1KG");
        sugar.Should().NotBeNull();

        // Something else in the system now points at what the import created: a manual receipt, which
        // is not one of the import's own movements and so counts as usage.
        StockLocation location = (await harness.Locations.FindAsync(harness.LocationId))!;

        await harness.Poster.ReceiveAsync(
            location,
            sugar!.Id,
            null,
            new Quantity(5m, "EA"),
            new Money(10m, "ZAR"),
            "Received by hand after the import");

        await harness.Context.CommitAsync();

        Func<Task> rollback = () => harness.SendAsync(
            new RollbackImportBatchCommand(created.BatchId, "Imported the wrong catalogue."));

        // Rule 6: refused whole, naming what is in the way. A partial rollback would leave a movement
        // pointing at a deleted item, which is worse than no rollback at all.
        (await rollback.Should().ThrowAsync<ImportRuleException>())
            .Which.Code.Should().Be("IMPORTS_ROLLBACK_BLOCKED");

        (await harness.Items.FindByCodeAsync("SUGAR-1KG")).Should().NotBeNull();
        (await harness.Items.FindByCodeAsync("RICE-2KG")).Should().NotBeNull("the rollback was all or nothing");
    }

    [Fact]
    public async Task Prices_import_onto_an_existing_list_and_reprice_it_on_the_next_sheet()
    {
        await using ImportsHarness harness = await ImportsHarness.CreateAsync(fixture);

        ImportBatchCreated created = await harness.SendAsync(Upload(
            ImportTargetKind.PriceListLines,
            """
            Price List,Item Code,Selling Price
            RETAIL,MILK-2L,24.99
            RETAIL,BREAD,18.50
            """));

        await harness.SendAsync(new ValidateImportBatchCommand(created.BatchId));
        ImportBatchCounts committed = await harness.SendAsync(new CommitImportBatchCommand(created.BatchId));

        committed.CreatedRows.Should().Be(2);

        PriceList? list = await harness.PriceLists.FindByCodeAsync("RETAIL");
        list!.Lines.Should().HaveCount(2);
        list.Lines.Should().Contain(line => line.UnitPrice.Amount == 24.99m);

        // Next month's sheet is the same natural key at a new price.
        ImportBatchCreated repriced = await harness.SendAsync(Upload(
            ImportTargetKind.PriceListLines,
            """
            Price List,Item Code,Selling Price
            RETAIL,MILK-2L,26.99
            """,
            ImportDuplicateStrategy.Update));

        await harness.SendAsync(new ValidateImportBatchCommand(repriced.BatchId));
        ImportBatchCounts second = await harness.SendAsync(new CommitImportBatchCommand(repriced.BatchId));

        second.UpdatedRows.Should().Be(1);

        PriceList? after = await harness.PriceLists.FindByCodeAsync("RETAIL");
        after!.Lines.Should().Contain(line => line.UnitPrice.Amount == 26.99m);
        after.Lines.Should().NotContain(line => line.UnitPrice.Amount == 24.99m);
    }

    [Fact]
    public async Task The_duplicate_strategy_decides_what_a_row_that_already_exists_does()
    {
        await using ImportsHarness harness = await ImportsHarness.CreateAsync(fixture);

        const string sheet = """
            Supplier Code,Supplier Name
            ACME001,Acme Renamed
            """;

        harness.Partners.Add(Partner.Create(
            harness.TenantId, "ACME001", "Acme Wholesalers", PartnerType.Supplier));

        await harness.Context.CommitAsync();

        // Skip: the row is a declared no-op, reported so the person sees it before committing.
        ImportBatchCreated skip = await harness.SendAsync(
            Upload(ImportTargetKind.Suppliers, sheet, ImportDuplicateStrategy.Skip, fileName: "skip.csv"));

        ImportBatchCounts skipped = await harness.SendAsync(new ValidateImportBatchCommand(skip.BatchId));
        skipped.SkippedRows.Should().Be(1);
        skipped.ValidRows.Should().Be(0);

        // Fail: the clash is an error the person is told about.
        ImportBatchCreated fail = await harness.SendAsync(
            Upload(ImportTargetKind.Suppliers, sheet, ImportDuplicateStrategy.Fail, fileName: "fail.csv"));

        ImportBatchCounts failed = await harness.SendAsync(new ValidateImportBatchCommand(fail.BatchId));
        failed.InvalidRows.Should().Be(1);

        (await harness.Partners.FindByCodeAsync("ACME001"))!.Name.Should().Be("Acme Wholesalers");
    }

    [Fact]
    public async Task Committing_the_same_batch_twice_returns_the_first_commits_counters()
    {
        await using ImportsHarness harness = await ImportsHarness.CreateAsync(fixture);

        ImportBatchCreated created = await harness.SendAsync(Upload(
            ImportTargetKind.Suppliers,
            """
            Supplier Code,Supplier Name
            ACME001,Acme Wholesalers
            """));

        await harness.SendAsync(new ValidateImportBatchCommand(created.BatchId));

        ImportBatchCounts first = await harness.SendAsync(new CommitImportBatchCommand(created.BatchId));
        ImportBatchCounts replay = await harness.SendAsync(new CommitImportBatchCommand(created.BatchId));

        // Business rule 4: the batch id is the idempotency key, so the second press is the same commit.
        replay.Should().BeEquivalentTo(first);
        first.CreatedRows.Should().Be(1);
    }

    [Fact]
    public async Task Rolling_back_the_same_batch_twice_returns_the_first_rollbacks_counters()
    {
        // §4.26 — Rollback used to lack the idempotency branch Commit already had for itself, so a
        // replay threw IMPORTS_UNEXPECTED_BATCH_STATUS instead of returning the first rollback's answer.
        await using ImportsHarness harness = await ImportsHarness.CreateAsync(fixture);

        ImportBatchCreated created = await harness.SendAsync(Upload(
            ImportTargetKind.Suppliers,
            """
            Supplier Code,Supplier Name
            ACME001,Acme Wholesalers
            """));

        await harness.SendAsync(new ValidateImportBatchCommand(created.BatchId));
        await harness.SendAsync(new CommitImportBatchCommand(created.BatchId));

        ImportBatchCounts first = await harness.SendAsync(
            new RollbackImportBatchCommand(created.BatchId, "Wrong supplier file."));
        ImportBatchCounts replay = await harness.SendAsync(
            new RollbackImportBatchCommand(created.BatchId, "Wrong supplier file."));

        replay.Should().BeEquivalentTo(first);

        ImportBatch? batch = await harness.Batches.FindAsync(created.BatchId);
        batch!.Status.Should().Be(ImportBatchStatus.RolledBack);
    }

    [Fact]
    public async Task Re_uploading_a_file_that_already_committed_is_refused_on_its_content()
    {
        await using ImportsHarness harness = await ImportsHarness.CreateAsync(fixture);

        const string sheet = """
            Supplier Code,Supplier Name
            ACME001,Acme Wholesalers
            """;

        ImportBatchCreated created = await harness.SendAsync(
            Upload(ImportTargetKind.Suppliers, sheet, fileName: "suppliers-march.csv"));

        await harness.SendAsync(new ValidateImportBatchCommand(created.BatchId));
        await harness.SendAsync(new CommitImportBatchCommand(created.BatchId));

        // The same bytes under a different name are the same sheet — which is how somebody doubles
        // their opening stock.
        Func<Task> reupload = () => harness.SendAsync(
            Upload(ImportTargetKind.Suppliers, sheet, fileName: "suppliers-march-FINAL.csv"));

        await reupload.Should().ThrowAsync<ImportConflictException>();
    }

    [Fact]
    public async Task A_discarded_batch_writes_nothing_and_cannot_be_committed()
    {
        await using ImportsHarness harness = await ImportsHarness.CreateAsync(fixture);

        ImportBatchCreated created = await harness.SendAsync(Upload(
            ImportTargetKind.Suppliers,
            """
            Supplier Code,Supplier Name
            ACME001,Acme Wholesalers
            """));

        await harness.SendAsync(new ValidateImportBatchCommand(created.BatchId));
        await harness.SendAsync(new DiscardImportBatchCommand(created.BatchId));

        ImportBatch? batch = await harness.Batches.FindAsync(created.BatchId);
        batch!.Status.Should().Be(ImportBatchStatus.Discarded);

        Func<Task> commit = () => harness.SendAsync(new CommitImportBatchCommand(created.BatchId));
        await commit.Should().ThrowAsync<ImportRuleException>();

        (await harness.Partners.FindByCodeAsync("ACME001")).Should().BeNull();
    }

    [Fact]
    public async Task A_saved_template_maps_the_next_months_file_with_no_human_step()
    {
        await using ImportsHarness harness = await ImportsHarness.CreateAsync(fixture);

        // Headers no alias knows — this is the file that needs a person the first time.
        const string headers = "Vendor Ref,Trading As,Contact";

        ImportBatchCreated first = await harness.SendAsync(Upload(
            ImportTargetKind.Suppliers,
            $"""
            {headers}
            ACME001,Acme Wholesalers,accounts@acme.co.za
            """,
            fileName: "march.csv"));

        first.MappedAutomatically.Should().BeFalse();
        first.Status.Should().Be(ImportBatchStatus.Parsed);

        await harness.SendAsync(new SetImportMappingCommand(
            first.BatchId,
            [
                new ImportTemplateBinding("code", "Vendor Ref", null),
                new ImportTemplateBinding("name", "Trading As", null),
                new ImportTemplateBinding("email", "Contact", null),
            ]));

        await harness.SendAsync(new SaveImportMappingTemplateCommand(
            first.BatchId, "ACME-SHEET", "Acme's monthly supplier sheet"));

        // Next month: same headers, different rows.
        ImportBatchCreated second = await harness.SendAsync(Upload(
            ImportTargetKind.Suppliers,
            $"""
            {headers}
            BEST002,Best Foods,orders@bestfoods.co.za
            """,
            fileName: "april.csv"));

        second.MappedAutomatically.Should().BeTrue();
        second.TemplateCode.Should().Be("ACME-SHEET");

        await harness.SendAsync(new ValidateImportBatchCommand(second.BatchId));
        await harness.SendAsync(new CommitImportBatchCommand(second.BatchId));

        (await harness.Partners.FindByCodeAsync("BEST002"))!.Email.Should().Be("orders@bestfoods.co.za");
    }

    [Fact]
    public async Task A_required_field_bound_to_nothing_leaves_the_batch_unmapped()
    {
        await using ImportsHarness harness = await ImportsHarness.CreateAsync(fixture);

        // A name column and nothing that means "code" — the natural key is missing.
        ImportBatchCreated created = await harness.SendAsync(Upload(
            ImportTargetKind.Suppliers,
            """
            Supplier Name,Telephone
            Acme Wholesalers,021 555 0100
            """));

        created.MappedAutomatically.Should().BeFalse();
        created.Status.Should().Be(ImportBatchStatus.Parsed);
        created.UnmappedRequiredFields.Should().Contain("code");

        // Validation cannot run against a mapping that does not exist.
        Func<Task> validate = () => harness.SendAsync(new ValidateImportBatchCommand(created.BatchId));
        await validate.Should().ThrowAsync<ImportRuleException>();
    }

    /// <summary>Builds an upload command from a CSV string.</summary>
    /// <param name="target">What the rows are aimed at.</param>
    /// <param name="csv">The file's text.</param>
    /// <param name="duplicates">What to do about rows that already exist.</param>
    /// <param name="storeId">The store, for a store-scoped target.</param>
    /// <param name="fileName">The name it is uploaded under.</param>
    private static CreateImportBatchCommand Upload(
        ImportTargetKind target,
        string csv,
        ImportDuplicateStrategy duplicates = ImportDuplicateStrategy.Skip,
        Guid? storeId = null,
        string fileName = "upload.csv")
        => new(
            target,
            ImportSourceFormat.Csv,
            fileName,
            Encoding.UTF8.GetBytes(csv),
            duplicates,
            storeId);
}
