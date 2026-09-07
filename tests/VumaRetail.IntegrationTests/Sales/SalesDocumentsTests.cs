using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Catalog;
using VumaRetail.Application.Sales.Commands.Analytics;
using VumaRetail.Application.Sales.Commands.Invoices;
using VumaRetail.Application.Sales.Commands.Quotes;
using VumaRetail.Application.Sales.Queries;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Sales;
using VumaRetail.Domain.Sales.Analytics;
using VumaRetail.Domain.Sales.Invoices;
using VumaRetail.Domain.Sales.Quotes;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Sales;

/// <summary>
/// Stage 10c against real PostgreSQL: one order split into one posted invoice per company, quote
/// snapshots that survive a mid-process reprice, company-scoped reads, and analytics that stay up
/// when the saga coordinator lags.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SalesDocumentsTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Multi_company_order_posts_one_invoice_per_company_with_pack_sizes_and_a_shared_reference()
    {
        await using SalesDocumentsHarness harness = await SalesDocumentsHarness.CreateAsync(fixture);

        Guid sourceLineA = UuidV7.NewGuid();
        Guid sourceLineB = UuidV7.NewGuid();

        // The demo split: 60 units from Company A in cases of 10, 40 from Company B as eaches.
        // Tax at 15% on R100 a unit: A carries 6000/900/6900, B carries 4000/600/4600.
        var request = new InvoiceIssuingRequest(
            harness.TenantId,
            harness.CompanyAId,
            Guid.NewGuid(),
            "SO-2026-000412",
            InvoiceSourceType.Order,
            harness.CustomerId,
            "ZAR",
            [
                new InvoiceCompanySegment(harness.CompanyAId,
                [
                    new InvoiceLineInput(
                        harness.ItemId, null, 60m, "EA", 100m, 0m, 900m,
                        "6 x Case of 10", null, sourceLineA),
                ]),
                new InvoiceCompanySegment(harness.CompanyBId,
                [
                    new InvoiceLineInput(
                        harness.ItemId, null, 40m, "EA", 100m, 0m, 600m,
                        "40 x Each", null, sourceLineB),
                ]),
            ],
            "SO-2026-000412",
            "issue-so-000412",
            "user:thandi");

        IReadOnlyList<IssuedInvoice> issued = await harness.CreateIssuingService().IssueAsync(request);

        // Two invoices, one per company, with distinct numbers from per-company INV series.
        issued.Should().HaveCount(2);
        issued.Select(invoice => invoice.CompanyId).Should().BeEquivalentTo(
            new[] { harness.CompanyAId, harness.CompanyBId });
        issued[0].InvoiceNumber.Should().NotBe(issued[1].InvoiceNumber);

        Guid invoiceAId = issued.First(invoice => invoice.CompanyId == harness.CompanyAId).InvoiceId;
        Guid invoiceBId = issued.First(invoice => invoice.CompanyId == harness.CompanyBId).InvoiceId;

        await using (var read = harness.OpenCompanyDb())
        {
            var invoices = new InvoiceRepository(read);

            Invoice invoiceA = (await invoices.FindAsync(invoiceAId))!;
            invoiceA.Status.Should().Be(InvoiceStatus.Posted);
            invoiceA.PostedAt.Should().NotBeNull();
            invoiceA.CompanyId.Should().Be(harness.CompanyAId);
            invoiceA.GroupDocumentRef.Should().Be("SO-2026-000412");
            invoiceA.Net.Amount.Should().Be(6000m);
            invoiceA.Tax.Amount.Should().Be(900m);
            invoiceA.Gross.Amount.Should().Be(6900m);
            invoiceA.Lines.Should().HaveCount(1);
            invoiceA.Lines[0].PackSizeDescription.Should().Be("6 x Case of 10");
            invoiceA.Lines[0].QuantityValue.Should().Be(60m);

            Invoice invoiceB = (await invoices.FindAsync(invoiceBId))!;
            invoiceB.Status.Should().Be(InvoiceStatus.Posted);
            invoiceB.CompanyId.Should().Be(harness.CompanyBId);
            invoiceB.GroupDocumentRef.Should().Be("SO-2026-000412");
            invoiceB.Net.Amount.Should().Be(4000m);
            invoiceB.Tax.Amount.Should().Be(600m);
            invoiceB.Gross.Amount.Should().Be(4600m);
            invoiceB.Lines.Should().HaveCount(1);
            invoiceB.Lines[0].PackSizeDescription.Should().Be("40 x Each");

            // Line for line and cent for cent: the two segments reconcile to the order.
            (invoiceA.Net.Amount + invoiceB.Net.Amount).Should().Be(10000m);
            (invoiceA.Tax.Amount + invoiceB.Tax.Amount).Should().Be(1500m);
            (invoiceA.Gross.Amount + invoiceB.Gross.Amount).Should().Be(11500m);
            (invoiceA.Lines[0].QuantityValue + invoiceB.Lines[0].QuantityValue).Should().Be(100m);
        }

        // The saga intent completed with both legs acknowledged, and every posted invoice raised
        // exactly one financial event carrying its exact amounts.
        SagaIntent intent = await harness.Registry.SagaIntents
            .Include(saga => saga.Legs)
            .FirstAsync(saga => saga.IdempotencyKey == "issue-so-000412");
        intent.State.Should().Be(SagaIntentState.Completed);
        intent.Legs.Should().HaveCount(2);
        intent.Legs.Should().OnlyContain(leg => leg.State == SagaLegState.Acknowledged);

        harness.InvoiceEvents.Events.Should().HaveCount(2);
        harness.InvoiceEvents.Events.Select(posted => posted.Gross.Amount).Should()
            .BeEquivalentTo(new[] { 6900m, 4600m });

        // Replaying the same key returns the same invoices without writing a third.
        IReadOnlyList<IssuedInvoice> replayed = await harness.CreateIssuingService().IssueAsync(request);
        replayed.Select(invoice => invoice.InvoiceId).Should()
            .BeEquivalentTo(issued.Select(invoice => invoice.InvoiceId));

        await using (var read = harness.OpenCompanyDb())
        {
            int count = await read.Invoices.CountAsync(invoice => invoice.SourceDocumentRef == request.SourceDocumentId.ToString());
            count.Should().Be(2);

            int outbox = await read.OutboxMessages.CountAsync(message =>
                message.EntityType == nameof(Invoice) || message.EntityType == nameof(InvoiceLine));
            outbox.Should().BeGreaterThanOrEqualTo(4);
        }
    }

    [Fact]
    public async Task Quote_lifecycle_snapshots_prices_against_a_mid_process_reprice()
    {
        await using SalesDocumentsHarness harness = await SalesDocumentsHarness.CreateAsync(fixture);

        var numbers = new DocumentNumberSequence(harness.Context, harness.TenantContext);
        var actingCompany = new AmbientCompanyContext();

        var create = new CreateQuoteCommandHandler(
            harness.Quotes, harness.TenantContext, actingCompany, numbers, harness.Clock);
        var addLine = new AddQuoteLineCommandHandler(
            harness.Quotes,
            new Application.Pos.SellableItemResolver(
                new ItemRepository(harness.Context),
                new ItemVariantRepository(harness.Context),
                new UnitOfMeasureRepository(harness.Context),
                new BarcodeRepository(harness.Context)),
            new Application.Sales.Pricing.PriceResolver(
                new PriceListRepository(harness.Context),
                new PromotionRepository(harness.Context)),
            new VumaRetail.Finance.Tax.TaxEngine(new TaxRuleRepository(harness.Context)),
            new Infrastructure.Sales.PackSizeResolver(new UnitOfMeasureRepository(harness.Context)),
            harness.Clock);

        DateOnly today = DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime);

        Guid quoteId = await create.HandleAsync(new CreateQuoteCommand(
            harness.CustomerId, "ZAR", today.AddDays(7), null, harness.CompanyAId));
        await harness.Context.CommitAsync();

        Guid lineId = await addLine.HandleAsync(new AddQuoteLineCommand(
            quoteId, harness.ItemId, null, 2m, "EA"));
        await harness.Context.CommitAsync();
        lineId.Should().NotBe(Guid.Empty);

        Quote quoted = (await harness.Quotes.FindAsync(quoteId))!;
        quoted.Lines.Should().HaveCount(1);
        quoted.Lines[0].UnitPrice.Amount.Should().Be(100m);
        quoted.Lines[0].TaxAmount.Amount.Should().Be(30m);
        quoted.Lines[0].PackSizeDescription.Should().Be("Each");
        quoted.Net.Amount.Should().Be(200m);
        quoted.Tax.Amount.Should().Be(30m);
        quoted.Gross.Amount.Should().Be(230m);

        // Mid-process, the shop reprices to R120. The live quote must not move.
        var repricer = new Application.Sales.Commands.SetPriceListLineCommandHandler(
            new PriceListRepository(harness.Context));
        PriceList? retail = await new PriceListRepository(harness.Context).FindByCodeAsync("RETAIL");
        retail.Should().NotBeNull();
        await repricer.HandleAsync(new Application.Sales.Commands.SetPriceListLineCommand(
            retail!.Id, harness.ItemId, null, new Money(120m, "ZAR"), 1m));
        await harness.Context.CommitAsync();

        Quote frozen = (await harness.Quotes.FindAsync(quoteId))!;
        frozen.Lines[0].UnitPrice.Amount.Should().Be(100m);
        frozen.Lines[0].TaxAmount.Amount.Should().Be(30m);
        frozen.Gross.Amount.Should().Be(230m);

        var issue = new IssueQuoteCommandHandler(harness.Quotes, harness.Clock);
        await issue.HandleAsync(new IssueQuoteCommand(quoteId));
        await harness.Context.CommitAsync();

        var accept = new AcceptQuoteCommandHandler(harness.Quotes, harness.Clock);
        await accept.HandleAsync(new AcceptQuoteCommand(quoteId));
        await harness.Context.CommitAsync();

        var convert = new ConvertQuoteToOrderCommandHandler(harness.Quotes);
        Guid converted = await convert.HandleAsync(new ConvertQuoteToOrderCommand(quoteId));
        await harness.Context.CommitAsync();
        converted.Should().Be(quoteId);

        Quote done = (await harness.Quotes.FindAsync(quoteId))!;
        done.Status.Should().Be(QuoteStatus.Converted);

        // One acceptance is one conversion: the second attempt fails.
        Func<Task> again = () => convert.HandleAsync(new ConvertQuoteToOrderCommand(quoteId));
        await again.Should().ThrowAsync<QuotesRuleException>();
    }

    [Fact]
    public async Task Finalize_posts_a_draft_and_raises_its_exact_financial_event()
    {
        await using SalesDocumentsHarness harness = await SalesDocumentsHarness.CreateAsync(fixture);

        var numbers = new DocumentNumberSequence(harness.Context, harness.TenantContext);
        var actingCompany = new AmbientCompanyContext();

        var create = new CreateInvoiceCommandHandler(
            harness.Invoices, harness.TenantContext, actingCompany, numbers);
        Guid invoiceId = await create.HandleAsync(new CreateInvoiceCommand(
            harness.CustomerId,
            "ZAR",
            Guid.NewGuid(),
            InvoiceSourceType.Sale,
            [
                new InvoiceLineInput(harness.ItemId, null, 2m, "EA", 100m, 0m, 30m, "Each"),
            ],
            null,
            harness.CompanyAId));
        await harness.Context.CommitAsync();

        Invoice draft = (await harness.Invoices.FindAsync(invoiceId))!;
        draft.Status.Should().Be(InvoiceStatus.Draft);

        var finalize = new FinalizeInvoiceCommandHandler(
            harness.Invoices, harness.InvoiceEvents, harness.Clock);
        await finalize.HandleAsync(new FinalizeInvoiceCommand(invoiceId));
        await harness.Context.CommitAsync();

        Invoice posted = (await harness.Invoices.FindAsync(invoiceId))!;
        posted.Status.Should().Be(InvoiceStatus.Posted);
        posted.PostedAt.Should().NotBeNull();
        posted.Gross.Amount.Should().Be(230m);

        harness.InvoiceEvents.Events.Should().HaveCount(1);
        Application.Sales.InvoicePostedEvent posted_event = harness.InvoiceEvents.Events[0];
        posted_event.InvoiceId.Should().Be(invoiceId);
        posted_event.CompanyId.Should().Be(harness.CompanyAId);
        posted_event.Net.Amount.Should().Be(200m);
        posted_event.Tax.Amount.Should().Be(30m);
        posted_event.Gross.Amount.Should().Be(230m);

        // Posted is posted: cancelling now is refused.
        var cancel = new CancelInvoiceCommandHandler(harness.Invoices);
        Func<Task> refuse = () => cancel.HandleAsync(new CancelInvoiceCommand(invoiceId));
        await refuse.Should().ThrowAsync<InvoicesRuleException>();
    }

    [Fact]
    public async Task Company_scoping_keeps_one_companys_invoices_invisible_to_the_other()
    {
        await using SalesDocumentsHarness harness = await SalesDocumentsHarness.CreateAsync(fixture);

        var numbers = new DocumentNumberSequence(harness.Context, harness.TenantContext);
        var create = new CreateInvoiceCommandHandler(
            harness.Invoices, harness.TenantContext, new AmbientCompanyContext(), numbers);

        Guid invoiceB = await create.HandleAsync(new CreateInvoiceCommand(
            harness.CustomerId, "ZAR", Guid.NewGuid(), InvoiceSourceType.Sale,
            [new InvoiceLineInput(harness.ItemId, null, 1m, "EA", 100m, 0m, 15m, "Each")],
            null, harness.CompanyBId));
        await harness.Context.CommitAsync();

        // Acting for Company A: Company B's invoice does not exist as far as this caller knows.
        var boundToA = new AmbientCompanyContext();
        boundToA.SetCompany(harness.CompanyAId);

        var get = new GetInvoiceQueryHandler(harness.Invoices, boundToA);
        Func<Task> read = () => get.HandleAsync(new GetInvoiceQuery(invoiceB));
        await read.Should().ThrowAsync<InvoicesNotFoundException>();

        var list = new ListInvoicesQueryHandler(harness.Invoices, boundToA);
        Func<Task> cross = () => list.HandleAsync(new ListInvoicesQuery(harness.CompanyBId));
        await cross.Should().ThrowAsync<InvoicesRuleException>();

        // And Company A's own list does not contain it.
        IReadOnlyList<Invoice> mine = await list.HandleAsync(new ListInvoicesQuery(harness.CompanyAId));
        mine.Should().NotContain(invoice => invoice.Id == invoiceB);
    }

    [Fact]
    public async Task Analytics_rebuild_rolls_daily_facts_to_months_and_survives_a_lagging_saga()
    {
        await using SalesDocumentsHarness harness = await SalesDocumentsHarness.CreateAsync(fixture);

        var numbers = new DocumentNumberSequence(harness.Context, harness.TenantContext);
        var actingCompany = new AmbientCompanyContext();
        var create = new CreateInvoiceCommandHandler(
            harness.Invoices, harness.TenantContext, actingCompany, numbers);
        var finalize = new FinalizeInvoiceCommandHandler(
            harness.Invoices, harness.InvoiceEvents, harness.Clock);

        for (int day = 1; day <= 3; day++)
        {
            Guid invoiceId = await create.HandleAsync(new CreateInvoiceCommand(
                harness.CustomerId, "ZAR", Guid.NewGuid(), InvoiceSourceType.Sale,
                [new InvoiceLineInput(harness.ItemId, null, day, "EA", 100m, 0m, 15m * day, "Each")],
                null, harness.CompanyAId));
            await harness.Context.CommitAsync();
            await finalize.HandleAsync(new FinalizeInvoiceCommand(invoiceId));
            await harness.Context.CommitAsync();
        }

        DateTimeOffset from = new(new DateOnly(2026, 9, 1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset to = new(new DateOnly(2026, 10, 1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Backdate the postings into September so the month bucket is deterministic. The raw
        // write bypasses the change tracker, so clear it — otherwise the rebuild below would
        // bucket the stale tracked instants instead of the committed ones.
        await harness.Context.Invoices
            .Where(invoice => invoice.CompanyId == harness.CompanyAId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                invoice => invoice.PostedAt,
                invoice => from.AddDays(1)));
        await harness.Context.CommitAsync();
        harness.Context.ChangeTracker.Clear();

        var rebuild = new RebuildAnalyticsCommandHandler(harness.Analytics);
        await rebuild.HandleAsync(new RebuildAnalyticsCommand(harness.CompanyAId, from, to));
        await harness.Context.CommitAsync();

        var companyQuery = new GetSalesAnalyticsQueryHandler(harness.Analytics);
        IReadOnlyList<SalesAnalytics> days = await companyQuery.HandleAsync(
            new GetSalesAnalyticsQuery(harness.CompanyAId, AnalyticsPeriod.Daily, from, to));
        days.Should().HaveCount(1);
        days[0].Revenue.Amount.Should().Be(690m);
        days[0].TaxLiability.Amount.Should().Be(90m);
        days[0].OrderCount.Should().Be(3);
        days[0].LineCount.Should().Be(3);

        IReadOnlyList<SalesAnalytics> months = await companyQuery.HandleAsync(
            new GetSalesAnalyticsQuery(harness.CompanyAId, AnalyticsPeriod.Monthly, from, to));
        months.Should().HaveCount(1);
        months[0].Period.Should().Be(AnalyticsPeriod.Monthly);
        months[0].Revenue.Amount.Should().Be(690m);

        // The saga coordinator is lagging with a stuck intent. The group read still answers —
        // analytics never block trade — stamped with AsAt, the way ADR-119 requires.
        harness.Registry.SagaIntents.Add(SagaIntent.Create(
            harness.TenantId, "sales.invoice-issue", "stuck-intent", harness.Clock.UtcNow, "{}"));
        await harness.Registry.SaveChangesAsync();

        var groupQuery = new GetGroupAnalyticsQueryHandler(harness.Analytics);
        IReadOnlyList<SalesAnalytics> group = await groupQuery.HandleAsync(
            new GetGroupAnalyticsQuery(AnalyticsPeriod.Daily, from, to));
        group.Should().NotBeEmpty();
        group.Should().OnlyContain(row => row.AsAt != default);
    }

    [Fact]
    public async Task A_missing_company_link_blocks_the_issue_before_anything_is_written()
    {
        await using SalesDocumentsHarness harness = await SalesDocumentsHarness.CreateAsync(fixture);

        var links = Substitute.For<ICompanyLinkGuard>();
        links.RequireLinkAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CompanyLinkScope>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("No active link."));

        var request = new InvoiceIssuingRequest(
            harness.TenantId,
            harness.CompanyAId,
            Guid.NewGuid(),
            "SO-2026-000413",
            InvoiceSourceType.Order,
            harness.CustomerId,
            "ZAR",
            [
                new InvoiceCompanySegment(harness.CompanyAId,
                [
                    new InvoiceLineInput(harness.ItemId, null, 1m, "EA", 100m, 0m, 15m, "Each"),
                ]),
                new InvoiceCompanySegment(harness.CompanyBId,
                [
                    new InvoiceLineInput(harness.ItemId, null, 1m, "EA", 100m, 0m, 15m, "Each"),
                ]),
            ],
            "SO-2026-000413",
            "issue-so-000413",
            "user:thandi");

        Func<Task> issue = () => harness.CreateIssuingService(links).IssueAsync(request);
        await issue.Should().ThrowAsync<InvalidOperationException>().WithMessage("*link*");

        await using (var read = harness.OpenCompanyDb())
        {
            int count = await read.Invoices.CountAsync();
            count.Should().Be(0);
        }

        bool intentExists = await harness.Registry.SagaIntents
            .AnyAsync(saga => saga.IdempotencyKey == "issue-so-000413");
        intentExists.Should().BeFalse();
    }

    [Fact]
    public async Task Reject_expire_and_convert_to_sale_walk_their_own_paths()
    {
        await using SalesDocumentsHarness harness = await SalesDocumentsHarness.CreateAsync(fixture);

        var numbers = new DocumentNumberSequence(harness.Context, harness.TenantContext);
        var actingCompany = new AmbientCompanyContext();
        DateOnly today = DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime);

        var create = new CreateQuoteCommandHandler(
            harness.Quotes, harness.TenantContext, actingCompany, numbers, harness.Clock);
        var addLine = new AddQuoteLineCommandHandler(
            harness.Quotes,
            new Application.Pos.SellableItemResolver(
                new ItemRepository(harness.Context),
                new ItemVariantRepository(harness.Context),
                new UnitOfMeasureRepository(harness.Context),
                new BarcodeRepository(harness.Context)),
            new Application.Sales.Pricing.PriceResolver(
                new PriceListRepository(harness.Context),
                new PromotionRepository(harness.Context)),
            new VumaRetail.Finance.Tax.TaxEngine(new TaxRuleRepository(harness.Context)),
            new Infrastructure.Sales.PackSizeResolver(new UnitOfMeasureRepository(harness.Context)),
            harness.Clock);

        Guid rejectedId = await create.HandleAsync(new CreateQuoteCommand(
            harness.CustomerId, "ZAR", today.AddDays(7), null, harness.CompanyAId));
        await harness.Context.CommitAsync();
        await addLine.HandleAsync(new AddQuoteLineCommand(rejectedId, harness.ItemId, null, 1m, "EA"));
        await harness.Context.CommitAsync();

        var issue = new IssueQuoteCommandHandler(harness.Quotes, harness.Clock);
        await issue.HandleAsync(new IssueQuoteCommand(rejectedId));
        await harness.Context.CommitAsync();

        var reject = new RejectQuoteCommandHandler(harness.Quotes);
        await reject.HandleAsync(new RejectQuoteCommand(rejectedId));
        await harness.Context.CommitAsync();

        Quote rejected = (await harness.Quotes.FindAsync(rejectedId))!;
        rejected.Status.Should().Be(QuoteStatus.Rejected);

        Guid convertedId = await create.HandleAsync(new CreateQuoteCommand(
            harness.CustomerId, "ZAR", today.AddDays(7), null, harness.CompanyAId));
        await harness.Context.CommitAsync();
        await addLine.HandleAsync(new AddQuoteLineCommand(convertedId, harness.ItemId, null, 1m, "EA"));
        await harness.Context.CommitAsync();
        await issue.HandleAsync(new IssueQuoteCommand(convertedId));
        await harness.Context.CommitAsync();

        var accept = new AcceptQuoteCommandHandler(harness.Quotes, harness.Clock);
        await accept.HandleAsync(new AcceptQuoteCommand(convertedId));
        await harness.Context.CommitAsync();

        var convertSale = new ConvertQuoteToSaleCommandHandler(harness.Quotes);
        await convertSale.HandleAsync(new ConvertQuoteToSaleCommand(convertedId));
        await harness.Context.CommitAsync();

        Quote converted = (await harness.Quotes.FindAsync(convertedId))!;
        converted.Status.Should().Be(QuoteStatus.Converted);

        Guid expiringId = await create.HandleAsync(new CreateQuoteCommand(
            harness.CustomerId, "ZAR", today.AddDays(7), null, harness.CompanyAId));
        await harness.Context.CommitAsync();

        var expire = new ExpireQuoteCommandHandler(harness.Quotes);
        await expire.HandleAsync(new ExpireQuoteCommand(expiringId));
        await harness.Context.CommitAsync();

        Quote expired = (await harness.Quotes.FindAsync(expiringId))!;
        expired.Status.Should().Be(QuoteStatus.Expired);
    }

    [Fact]
    public async Task Cancelling_a_draft_leaves_nothing_owed_and_nothing_posted()
    {
        await using SalesDocumentsHarness harness = await SalesDocumentsHarness.CreateAsync(fixture);

        var numbers = new DocumentNumberSequence(harness.Context, harness.TenantContext);
        var create = new CreateInvoiceCommandHandler(
            harness.Invoices, harness.TenantContext, new AmbientCompanyContext(), numbers);
        Guid invoiceId = await create.HandleAsync(new CreateInvoiceCommand(
            harness.CustomerId, "ZAR", Guid.NewGuid(), InvoiceSourceType.Sale,
            [new InvoiceLineInput(harness.ItemId, null, 1m, "EA", 100m, 0m, 15m, "Each")],
            null, harness.CompanyAId));
        await harness.Context.CommitAsync();

        var cancel = new CancelInvoiceCommandHandler(harness.Invoices);
        await cancel.HandleAsync(new CancelInvoiceCommand(invoiceId));
        await harness.Context.CommitAsync();

        Invoice cancelled = (await harness.Invoices.FindAsync(invoiceId))!;
        cancelled.Status.Should().Be(InvoiceStatus.Cancelled);
        harness.InvoiceEvents.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Document_queries_round_trip_quotes_and_invoices_with_their_lines()
    {
        await using SalesDocumentsHarness harness = await SalesDocumentsHarness.CreateAsync(fixture);

        var numbers = new DocumentNumberSequence(harness.Context, harness.TenantContext);
        var actingCompany = new AmbientCompanyContext();
        DateOnly today = DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime);

        var createQuote = new CreateQuoteCommandHandler(
            harness.Quotes, harness.TenantContext, actingCompany, numbers, harness.Clock);
        Guid quoteId = await createQuote.HandleAsync(new CreateQuoteCommand(
            harness.CustomerId, "ZAR", today.AddDays(7), null, harness.CompanyAId));
        await harness.Context.CommitAsync();

        var addLine = new AddQuoteLineCommandHandler(
            harness.Quotes,
            new Application.Pos.SellableItemResolver(
                new ItemRepository(harness.Context),
                new ItemVariantRepository(harness.Context),
                new UnitOfMeasureRepository(harness.Context),
                new BarcodeRepository(harness.Context)),
            new Application.Sales.Pricing.PriceResolver(
                new PriceListRepository(harness.Context),
                new PromotionRepository(harness.Context)),
            new VumaRetail.Finance.Tax.TaxEngine(new TaxRuleRepository(harness.Context)),
            new Infrastructure.Sales.PackSizeResolver(new UnitOfMeasureRepository(harness.Context)),
            harness.Clock);
        await addLine.HandleAsync(new AddQuoteLineCommand(quoteId, harness.ItemId, null, 2m, "EA"));
        await harness.Context.CommitAsync();

        var getQuote = new GetQuoteQueryHandler(harness.Quotes);
        Quote quote = await getQuote.HandleAsync(new GetQuoteQuery(quoteId));
        quote.Lines.Should().HaveCount(1);
        quote.Lines[0].PackSizeDescription.Should().Be("Each");

        var listQuotes = new ListQuotesQueryHandler(harness.Quotes);
        IReadOnlyList<Quote> mine = await listQuotes.HandleAsync(
            new ListQuotesQuery(null, harness.CustomerId));
        mine.Should().ContainSingle(q => q.Id == quoteId);

        IReadOnlyList<Quote> drafts = await listQuotes.HandleAsync(
            new ListQuotesQuery(QuoteStatus.Draft, null));
        drafts.Should().Contain(q => q.Id == quoteId);

        var createInvoice = new CreateInvoiceCommandHandler(
            harness.Invoices, harness.TenantContext, actingCompany, numbers);
        Guid invoiceId = await createInvoice.HandleAsync(new CreateInvoiceCommand(
            harness.CustomerId, "ZAR", Guid.NewGuid(), InvoiceSourceType.Quote,
            [new InvoiceLineInput(harness.ItemId, null, 2m, "EA", 100m, 0m, 30m, "Each")],
            null, harness.CompanyAId));
        await harness.Context.CommitAsync();

        var getInvoice = new GetInvoiceQueryHandler(harness.Invoices, actingCompany);
        Invoice invoice = await getInvoice.HandleAsync(new GetInvoiceQuery(invoiceId));
        invoice.Lines.Should().HaveCount(1);
        invoice.Lines[0].PackSizeDescription.Should().Be("Each");
        invoice.Gross.Amount.Should().Be(230m);

        var listInvoices = new ListInvoicesQueryHandler(harness.Invoices, actingCompany);
        IReadOnlyList<Invoice> companyInvoices = await listInvoices.HandleAsync(
            new ListInvoicesQuery(harness.CompanyAId));
        companyInvoices.Should().ContainSingle(i => i.Id == invoiceId);
    }
}

