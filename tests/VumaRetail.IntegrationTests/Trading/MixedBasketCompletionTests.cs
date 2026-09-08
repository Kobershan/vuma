using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Registry.Trading;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry.Trading;
using VumaRetail.Domain.Sales;
using VumaRetail.Domain.Sales.Invoices;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Trading;

/// <summary>
/// Stage 09b against real PostgreSQL: the operator's basket (2 × R799.00 hot plate, 3 ×
/// R100.33 gloves from Noortgats; 1 × R214.00 maize from Siyaya, VAT 15% inclusive) becomes
/// one tax invoice per company, each in its own books, from one till and one tender.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MixedBasketCompletionTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Mixed_basket_produces_one_tax_invoice_per_company()
    {
        await using TradingBasketHarness harness = await TradingBasketHarness.CreateAsync(fixture);
        TradingBasketHarness.TradingHandlers till = harness.Handlers();

        Guid sessionId = await till.OpenAsync(new OpenTradingSessionCommand(
            harness.PremisesId, harness.TerminalId, harness.CashierId, harness.CompanyAId,
            "ZAR", $"TS-{Guid.NewGuid():N}", harness.CustomerId));
        await till.AddLineAsync(new AddBasketLineCommand(sessionId, "HOTPLATE-2", 2m, "EA", 799.00m, "ZAR"));
        await till.AddLineAsync(new AddBasketLineCommand(sessionId, "GLOVE-WL", 3m, "EA", 100.33m, "ZAR"));
        await till.AddLineAsync(new AddBasketLineCommand(sessionId, "MAIZE-10KG", 1m, "EA", 214.00m, "ZAR"));
        await till.CaptureTenderAsync(new CaptureTenderCommand(sessionId, "Card", 2112.99m, "ZAR", "AUTH-091"));

        MixedBasketCompletionService completion = harness.CreateCompletionService();
        IReadOnlyList<CompletedSegment> posted = await completion.CompleteAsync(sessionId);

        // Two invoices, two companies, two number sequences.
        posted.Should().HaveCount(2);
        posted.Select(p => p.CompanyId).Should().BeEquivalentTo([harness.CompanyAId, harness.CompanyBId]);
        posted.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.InvoiceNumber));

        await using var dbA = harness.OpenCompanyDb(harness.CompanyAId);
        await using var dbB = harness.OpenCompanyDb(harness.CompanyBId);

        // Each invoice's VAT is computed on its own segment: Noortgats R1 898.99 incl =
        // VAT R247.69 (the engine rounds line nets to cents: 1598 -> 1389.57 + 208.43,
        // 300.99 -> 261.73 + 39.26); Siyaya R214.00 incl = net R186.09 + VAT R27.91.
        Guid invoiceAId = posted.Single(p => p.CompanyId == harness.CompanyAId).InvoiceId;
        Guid invoiceBId = posted.Single(p => p.CompanyId == harness.CompanyBId).InvoiceId;
        Guid saleAId = posted.Single(p => p.CompanyId == harness.CompanyAId).SaleId;
        Guid saleBId = posted.Single(p => p.CompanyId == harness.CompanyBId).SaleId;
        Invoice invoiceA = await dbA.Invoices
            .FirstAsync(invoice => invoice.Id == invoiceAId);
        invoiceA.Status.Should().Be(InvoiceStatus.Posted);
        // Approximate: the invoice model prices exclusive while the till captures inclusive,
        // so the exclusive unit is derived back out and a repeating decimal leaves sub-cent
        // dust (here 0.0001 on R1 898.99 — a tenth of a cent, invisible at presentation
        // scale where both read R1898.99). ADR-145 records the rule.
        invoiceA.Gross.Amount.Should().BeApproximately(1898.99m, 0.001m);
        invoiceA.Tax.Amount.Should().BeApproximately(247.69m, 0.01m);
        invoiceA.CompanyId.Should().Be(harness.CompanyAId);

        Invoice invoiceB = await dbB.Invoices
            .FirstAsync(invoice => invoice.Id == invoiceBId);
        invoiceB.Status.Should().Be(InvoiceStatus.Posted);
        invoiceB.Gross.Amount.Should().Be(214.00m);
        invoiceB.Tax.Amount.Should().BeApproximately(27.91m, 0.01m);
        invoiceB.CompanyId.Should().Be(harness.CompanyBId);

        // And at presentation scale the two per-segment VAT figures do not equal the VAT of
        // the combined total computed as one document: R247.69 + R27.91 = R275.60 against
        // R275.61 for the single computation. Per-segment tax is the whole point (ADR-125),
        // and VAT returns are filed in cents, so cents are where the assertion lives.
        decimal sumOfRounded = invoiceA.Tax.RoundToCurrencyScale().Amount
            + invoiceB.Tax.RoundToCurrencyScale().Amount;
        sumOfRounded.Should().Be(275.60m);
        decimal roundingOfSingle = new Money(2112.99m, "ZAR").RoundToCurrencyScale().Amount;
        decimal singleVat = new Money(
            decimal.Round(2112.99m * 0.15m / 1.15m, 4), "ZAR").RoundToCurrencyScale().Amount;
        singleVat.Should().Be(275.61m);
        sumOfRounded.Should().NotBe(singleVat);

        // One completed sale per company, fully tendered by its own allocation.
        Sale saleA = await dbA.Sales
            .FirstAsync(sale => sale.Id == saleAId);
        Sale saleB = await dbB.Sales
            .FirstAsync(sale => sale.Id == saleBId);
        saleA.Status.Should().Be(SaleStatus.Completed);
        saleB.Status.Should().Be(SaleStatus.Completed);
        saleA.Gross.Amount.Should().Be(1898.99m);
        saleB.Gross.Amount.Should().Be(214.00m);

        // One fully-allocated receipt per segment, carrying the session as its group document.
        ArReceipt receiptA = await dbA.ArReceipts
            .Include(receipt => receipt.Allocations)
            .FirstAsync(receipt => receipt.GroupDocumentId == sessionId);
        ArReceipt receiptB = await dbB.ArReceipts
            .Include(receipt => receipt.Allocations)
            .FirstAsync(receipt => receipt.GroupDocumentId == sessionId);
        receiptA.Amount.Amount.Should().Be(1898.99m);
        receiptB.Amount.Amount.Should().Be(214.00m);
        receiptA.Allocations.Sum(allocation => allocation.Amount.Amount).Should().Be(receiptA.Amount.Amount);
        receiptB.Allocations.Sum(allocation => allocation.Amount.Amount).Should().Be(receiptB.Amount.Amount);

        // Per-company till sessions: cash-up splits because each segment settled in its own.
        TillSession tillA = await dbA.TillSessions
            .FirstAsync(till => till.TerminalId == harness.TerminalId && till.Status == TillSessionStatus.Open);
        TillSession tillB = await dbB.TillSessions
            .FirstAsync(till => till.TerminalId == harness.TerminalId && till.Status == TillSessionStatus.Open);
        tillA.CompanyId.Should().Be(harness.CompanyAId);
        tillB.CompanyId.Should().Be(harness.CompanyBId);

        // Journals posted in both companies: the sale, the invoice and the receipt each raised one.
        (await dbA.Journals.CountAsync()).Should().BeGreaterThanOrEqualTo(3);
        (await dbB.Journals.CountAsync()).Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Tender_allocation_is_cent_exact_and_deterministic()
    {
        await using TradingBasketHarness harness = await TradingBasketHarness.CreateAsync(fixture);
        TradingBasketHarness.TradingHandlers till = harness.Handlers();

        // R2 113.99 tendered over R1 899.00/R214.99-style segments: floor shares leave dust,
        // and the dust belongs to the larger segment, every run.
        Guid sessionId = await till.OpenAsync(new OpenTradingSessionCommand(
            harness.PremisesId, harness.TerminalId, harness.CashierId, harness.CompanyAId,
            "ZAR", $"TS-{Guid.NewGuid():N}", harness.CustomerId));
        await till.AddLineAsync(new AddBasketLineCommand(sessionId, "HOTPLATE-2", 2m, "EA", 799.00m, "ZAR"));
        await till.AddLineAsync(new AddBasketLineCommand(sessionId, "GLOVE-WL", 3m, "EA", 100.33m, "ZAR"));
        await till.AddLineAsync(new AddBasketLineCommand(sessionId, "MAIZE-10KG", 1m, "EA", 214.00m, "ZAR"));
        await till.CaptureTenderAsync(new CaptureTenderCommand(sessionId, "Card", 2113.99m, "ZAR", "AUTH-092"));

        TradingSession? session = await harness.Sessions.FindAsync(sessionId);
        session.Should().NotBeNull();

        decimal allocated = session!.Segments.Where(s => !s.IsRemoved).Sum(s => s.TenderAllocation!.Value.Amount);
        allocated.Should().Be(2113.99m, "R1.00 over the R2112.99 gross forces one cent of dust");

        decimal largerShare = session.Segments.Single(s => s.CompanyId == harness.CompanyAId).TenderAllocation!.Value.Amount;
        decimal smallerShare = session.Segments.Single(s => s.CompanyId == harness.CompanyBId).TenderAllocation!.Value.Amount;
        largerShare.Should().BeGreaterThan(smallerShare);

        // Deterministic: rebuild the same basket and the split is identical.
        Guid secondId = await till.OpenAsync(new OpenTradingSessionCommand(
            harness.PremisesId, harness.TerminalId, harness.CashierId, harness.CompanyAId,
            "ZAR", $"TS-{Guid.NewGuid():N}", harness.CustomerId));
        await till.AddLineAsync(new AddBasketLineCommand(secondId, "HOTPLATE-2", 2m, "EA", 799.00m, "ZAR"));
        await till.AddLineAsync(new AddBasketLineCommand(secondId, "GLOVE-WL", 3m, "EA", 100.33m, "ZAR"));
        await till.AddLineAsync(new AddBasketLineCommand(secondId, "MAIZE-10KG", 1m, "EA", 214.00m, "ZAR"));
        await till.CaptureTenderAsync(new CaptureTenderCommand(secondId, "Card", 2113.99m, "ZAR", "AUTH-093"));

        TradingSession? second = await harness.Sessions.FindAsync(secondId);
        second!.Segments.Single(s => s.CompanyId == harness.CompanyAId).TenderAllocation!.Value.Amount
            .Should().Be(largerShare);
    }

    [Fact]
    public async Task Completion_is_all_segments_or_none()
    {
        await using TradingBasketHarness harness = await TradingBasketHarness.CreateAsync(fixture);
        TradingBasketHarness.TradingHandlers till = harness.Handlers();

        // Company B has maize only on paper: drain it first so the second leg cannot reserve.
        await DrainStockAsync(harness, harness.CompanyBId, harness.MaizeId, 100m);

        Guid sessionId = await till.OpenAsync(new OpenTradingSessionCommand(
            harness.PremisesId, harness.TerminalId, harness.CashierId, harness.CompanyAId,
            "ZAR", $"TS-{Guid.NewGuid():N}", harness.CustomerId));
        await till.AddLineAsync(new AddBasketLineCommand(sessionId, "HOTPLATE-2", 2m, "EA", 799.00m, "ZAR"));
        await till.AddLineAsync(new AddBasketLineCommand(sessionId, "MAIZE-10KG", 1m, "EA", 214.00m, "ZAR"));
        await till.CaptureTenderAsync(new CaptureTenderCommand(sessionId, "Card", 1812.00m, "ZAR", "AUTH-094"));

        MixedBasketCompletionService completion = harness.CreateCompletionService();
        Func<Task> act = () => completion.CompleteAsync(sessionId);

        await act.Should().ThrowAsync<TradingSessionFailedException>();

        TradingSession? session = await harness.Sessions.FindAsync(sessionId);
        session!.Status.Should().Be(TradingSessionStatus.CompletionFailed);
        session.FailureReason.Should().NotBeNullOrWhiteSpace();
        session.UnwoundInvoiceNumbers.Should().ContainSingle("the posted invoice is named, not hidden");

        await using var dbA = harness.OpenCompanyDb(harness.CompanyAId);
        await using var dbB = harness.OpenCompanyDb(harness.CompanyBId);

        // No sale stands: company A's sale was unwound by a full sales return...
        List<Sale> salesA = await dbA.Sales.ToListAsync();
        salesA.Should().ContainSingle("exactly one sale was posted before the failure");
        List<SalesReturn> returns = await dbA.SalesReturns.ToListAsync();
        returns.Should().ContainSingle("the posted leg has its reversing document");
        returns.Single().Status.Should().Be(SalesReturnStatus.Completed);

        // ...its receipt was reversed (the original stands — receipts are immutable — and the
        // reversal nets it to zero)...
        List<ArReceipt> receiptsA = await dbA.ArReceipts
            .Include(receipt => receipt.Allocations)
            .ToListAsync();
        receiptsA.Should().HaveCount(2, "the leg receipt plus its reversal");
        receiptsA.Sum(receipt => receipt.Amount.Amount).Should().Be(0m);
        receiptsA.Single(receipt => receipt.Amount.Amount < 0m).Amount.Amount
            .Should().Be(-1598.00m, "the negated R1 598.00 allocation");

        // ...and every hold the session took was released (chain-aware: a consumed chain's
        // born-Held row stays Held forever, so openness is the latest row per chain).
        int openHolds = await dbA.StockReservations
            .CountAsync(reservation => reservation.SourceDocumentId == sessionId
                && reservation.State == ReservationState.Held
                && !dbA.StockReservations.Any(terminal =>
                    terminal.ReservationId == reservation.ReservationId
                    && terminal.State != ReservationState.Held));
        openHolds.Should().Be(0);

        // Company B posted nothing at all.
        (await dbB.Sales.CountAsync()).Should().Be(0);
        (await dbB.Invoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Replayed_completion_returns_the_same_invoice_numbers()
    {
        await using TradingBasketHarness harness = await TradingBasketHarness.CreateAsync(fixture);
        TradingBasketHarness.TradingHandlers till = harness.Handlers();

        Guid sessionId = await till.OpenAsync(new OpenTradingSessionCommand(
            harness.PremisesId, harness.TerminalId, harness.CashierId, harness.CompanyAId,
            "ZAR", $"TS-{Guid.NewGuid():N}", harness.CustomerId));
        await till.AddLineAsync(new AddBasketLineCommand(sessionId, "HOTPLATE-2", 1m, "EA", 799.00m, "ZAR"));
        await till.AddLineAsync(new AddBasketLineCommand(sessionId, "MAIZE-10KG", 1m, "EA", 214.00m, "ZAR"));
        await till.CaptureTenderAsync(new CaptureTenderCommand(sessionId, "Card", 1013.00m, "ZAR", "AUTH-095"));

        MixedBasketCompletionService completion = harness.CreateCompletionService();
        IReadOnlyList<CompletedSegment> first = await completion.CompleteAsync(sessionId);
        IReadOnlyList<CompletedSegment> second = await completion.CompleteAsync(sessionId);
        IReadOnlyList<CompletedSegment> third = await completion.CompleteAsync(sessionId);

        second.Select(s => s.InvoiceNumber).Should().BeEquivalentTo(first.Select(s => s.InvoiceNumber));
        third.Select(s => s.InvoiceNumber).Should().BeEquivalentTo(first.Select(s => s.InvoiceNumber));

        await using var dbA = harness.OpenCompanyDb(harness.CompanyAId);
        await using var dbB = harness.OpenCompanyDb(harness.CompanyBId);
        (await dbA.Invoices.CountAsync() + await dbB.Invoices.CountAsync())
            .Should().Be(2, "three completions mint two invoices, not six");
        (await dbA.Sales.CountAsync() + await dbB.Sales.CountAsync()).Should().Be(2);
        (await dbA.ArReceipts.CountAsync() + await dbB.ArReceipts.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Offline_mixed_basket_replays_once()
    {
        await using TradingBasketHarness harness = await TradingBasketHarness.CreateAsync(fixture);
        TradingBasketHarness.TradingHandlers till = harness.Handlers();

        // The till mints every id offline and replays the same batch twice after reconnect:
        // same session key, same line ids, same tender — one basket, not two.
        string key = $"TS-OFFLINE-{Guid.NewGuid():N}";
        Guid hotLine = UuidV7.NewGuid();
        Guid maizeLine = UuidV7.NewGuid();

        for (int replay = 0; replay < 2; replay++)
        {
            Guid sessionId = await till.OpenAsync(new OpenTradingSessionCommand(
                harness.PremisesId, harness.TerminalId, harness.CashierId, harness.CompanyAId,
                "ZAR", key, harness.CustomerId));
            await till.AddLineAsync(new AddBasketLineCommand(
                sessionId, "HOTPLATE-2", 1m, "EA", 799.00m, "ZAR", LineId: hotLine));
            await till.AddLineAsync(new AddBasketLineCommand(
                sessionId, "MAIZE-10KG", 1m, "EA", 214.00m, "ZAR", LineId: maizeLine));
            await till.CaptureTenderAsync(new CaptureTenderCommand(sessionId, "Card", 1013.00m, "ZAR", "AUTH-096"));

            MixedBasketCompletionService completion = harness.CreateCompletionService();
            await completion.CompleteAsync(sessionId);
        }

        await using var dbA = harness.OpenCompanyDb(harness.CompanyAId);
        await using var dbB = harness.OpenCompanyDb(harness.CompanyBId);
        (await dbA.Invoices.CountAsync() + await dbB.Invoices.CountAsync())
            .Should().Be(2, "two batch replays land two invoices, not four");
        (await dbA.Sales.CountAsync() + await dbB.Sales.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Single_company_basket_does_not_touch_the_registry()
    {
        await using TradingBasketHarness harness = await TradingBasketHarness.CreateAsync(fixture);
        TradingBasketHarness.TradingHandlers till = harness.Handlers();

        Guid sessionId = await till.OpenAsync(new OpenTradingSessionCommand(
            harness.PremisesId, harness.TerminalId, harness.CashierId, harness.CompanyAId,
            "ZAR", $"TS-{Guid.NewGuid():N}", harness.CustomerId));
        await till.AddLineAsync(new AddBasketLineCommand(sessionId, "HOTPLATE-2", 1m, "EA", 799.00m, "ZAR"));
        await till.CaptureTenderAsync(new CaptureTenderCommand(sessionId, "Card", 799.00m, "ZAR", "AUTH-097"));

        MixedBasketCompletionService completion = harness.CreateCompletionService();
        IReadOnlyList<CompletedSegment> posted = await completion.CompleteAsync(sessionId);

        posted.Should().ContainSingle();
        (await harness.Registry.SagaIntents.CountAsync()).Should().Be(0, "rule 12: no registry path for one company");

        await using var dbA = harness.OpenCompanyDb(harness.CompanyAId);
        (await dbA.Sales.CountAsync()).Should().Be(1);
        (await dbA.Invoices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Voided_session_releases_every_reservation_and_posts_nothing()
    {
        await using TradingBasketHarness harness = await TradingBasketHarness.CreateAsync(fixture);
        TradingBasketHarness.TradingHandlers till = harness.Handlers();

        Guid sessionId = await till.OpenAsync(new OpenTradingSessionCommand(
            harness.PremisesId, harness.TerminalId, harness.CashierId, harness.CompanyAId,
            "ZAR", $"TS-{Guid.NewGuid():N}", harness.CustomerId));
        await till.AddLineAsync(new AddBasketLineCommand(sessionId, "HOTPLATE-2", 1m, "EA", 799.00m, "ZAR"));
        await till.AddLineAsync(new AddBasketLineCommand(sessionId, "MAIZE-10KG", 1m, "EA", 214.00m, "ZAR"));
        await till.CaptureTenderAsync(new CaptureTenderCommand(sessionId, "Card", 1013.00m, "ZAR", "AUTH-098"));
        await till.VoidSessionAsync(new VoidTradingSessionCommand(sessionId, "customer walked away"));

        TradingSession? session = await harness.Sessions.FindAsync(sessionId);
        session!.Status.Should().Be(TradingSessionStatus.Voided);

        await using var dbA = harness.OpenCompanyDb(harness.CompanyAId);
        await using var dbB = harness.OpenCompanyDb(harness.CompanyBId);
        (await dbA.Journals.CountAsync() + await dbB.Journals.CountAsync())
            .Should().Be(0, "a void posts nothing");
        (await dbA.Sales.CountAsync() + await dbB.Sales.CountAsync()).Should().Be(0);
        (await dbA.Invoices.CountAsync() + await dbB.Invoices.CountAsync()).Should().Be(0);
        (await dbA.StockReservations.CountAsync(
            reservation => reservation.SourceDocumentId == sessionId)).Should().Be(0);
    }

    /// <summary>Drains every unit of an item through a real issue, so the next reservation starves.</summary>
    private static async Task DrainStockAsync(
        TradingBasketHarness harness, Guid companyId, Guid itemId, decimal quantity)
    {
        await using VumaRetailDbContext context = harness.OpenCompanyDb(companyId);
        StockLocation? location = await context.StockLocations.FirstAsync();
        StockLedgerPoster poster = new(
            new StockBalanceRepository(context),
            new StockLedgerRepository(context),
            new NullValuationEventPublisher(),
            harness.Clock);
        await poster.IssueForSaleAsync(location, itemId, null, new Quantity(quantity, "EA"), Guid.NewGuid());
        await context.CommitAsync();
    }

    private sealed class NullValuationEventPublisher : IInventoryValuationEventPublisher
    {
        public Task PublishAsync(InventoryValuationEvent valuationEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
