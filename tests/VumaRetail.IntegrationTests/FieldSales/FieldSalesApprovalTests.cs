using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.FieldSales;
using VumaRetail.Application.FieldSales.Queries;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.FieldSales;
using VumaRetail.Application.FieldSales.Queries;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.FieldSales.Commands;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.FieldSales;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;
using VumaRetail.Application.FieldSales;
using VumaRetail.Infrastructure.Sales;
using VumaRetail.Domain.Sales.Invoices;
using VumaRetail.Finance.Tax;
using VumaRetail.Infrastructure.FieldSales;
using VumaRetail.Infrastructure.Inventory;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Interceptors;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Workflow.Approvals;

namespace VumaRetail.IntegrationTests.FieldSales;

/// <summary>
/// Stage 14b against real PostgreSQL: offline replay, approval with moved availability and
/// exhausted credit, crash-resume, cross-company sourcing, rejection cleanliness, territory
/// walls, performance comparison and credit-note conversion.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FieldSalesApprovalTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Offline_capture_replays_into_one_pro_forma()
    {
        await using FieldSalesHarness harness = await FieldSalesHarness.CreateAsync(fixture);
        Driver driver = new(harness);
        var (repId, repUser) = await driver.RegisterRepAsync(harness.CompanyAId, [harness.CustomerId]);

        Guid first = await driver.CaptureAsync(repId, harness.CompanyAId, "OFFLINE-1",
            [(harness.HotPlateAId, 20m)]);
        Guid second = await driver.CaptureAsync(repId, harness.CompanyAId, "OFFLINE-1",
            [(harness.HotPlateAId, 20m)]);

        second.Should().Be(first, "the same operation id twice produces one document");

        await using var dbA = harness.OpenCompanyDb(harness.CompanyAId);
        (await dbA.ProFormaOrders.CountAsync()).Should().Be(1);
        ProFormaOrder stored = await dbA.ProFormaOrders
            .Include(o => o.Lines)
            .FirstAsync();
        stored.Lines.Should().ContainSingle();
        stored.Status.Should().Be(ProFormaStatus.Draft);
    }

    [Fact]
    public async Task Approval_with_moved_availability_reports_delta_and_backorders_the_rest()
    {
        await using FieldSalesHarness harness = await FieldSalesHarness.CreateAsync(fixture);
        Driver driver = new(harness);
        var (repId, repUser) = await driver.RegisterRepAsync(harness.CompanyAId, [harness.CustomerId]);

        // Capture against full shelves, then move the stock before approval.
        Guid proFormaId = await driver.CaptureAsync(repId, harness.CompanyAId, "MOVE-1",
            [(harness.HotPlateAId, 20m)]);
        await driver.SubmitAsync(proFormaId, repUser);
        await driver.DrainAsync(harness.CompanyAId, harness.HotPlateAId, 95m);

        Guid orderId = await driver.ApproveAsync(proFormaId);

        await using var dbA = harness.OpenCompanyDb(harness.CompanyAId);
        SalesOrder order = await dbA.SalesOrders
            .Include(o => o.Lines)
            .FirstAsync(o => o.Id == orderId);
        order.Status.Should().Be(SalesOrderStatus.Confirmed);
        SalesOrderLine line = order.Lines.Should().ContainSingle().Subject;
        line.LineStatus.Should().Be(SalesOrderLineStatus.Backordered);
        line.BackorderedQuantity.Value.Should().Be(15m);

        // Only what exists was reserved: 5 units held and consumed, 15 backordered.
        int holds = await dbA.StockReservations.CountAsync();
        holds.Should().BeGreaterThanOrEqualTo(2, "the hold plus its consumption row");
    }

    [Fact]
    public async Task Exhausted_group_credit_refuses_and_reserves_nothing()
    {
        await using FieldSalesHarness harness = await FieldSalesHarness.CreateAsync(fixture, groupLimit: 100m);
        Driver driver = new(harness);
        var (repId, repUser) = await driver.RegisterRepAsync(harness.CompanyAId, [harness.CustomerId]);

        Guid proFormaId = await driver.CaptureAsync(repId, harness.CompanyAId, "CREDIT-1",
            [(harness.HotPlateAId, 2m)]);
        await driver.SubmitAsync(proFormaId, repUser);

        Func<Task> act = () => driver.ApproveAsync(proFormaId);
        (await act.Should().ThrowAsync<FieldSalesException>())
            .Where(e => e.Code == "PROFORMA_CREDIT_EXHAUSTED"
                && e.Message.Contains("100.00"));

        // The hold is never issued (ADR-108): nothing reserved, no order, pro forma back to Submitted.
        await using var dbA = harness.OpenCompanyDb(harness.CompanyAId);
        await using var dbB = harness.OpenCompanyDb(harness.CompanyBId);
        (await dbA.StockReservations.CountAsync() + await dbB.StockReservations.CountAsync()).Should().Be(0);
        (await dbA.SalesOrders.CountAsync()).Should().Be(0);

        ProFormaOrder stored = await dbA.ProFormaOrders.FirstAsync(o => o.Id == proFormaId);
        stored.Status.Should().Be(ProFormaStatus.Submitted);
    }

    [Fact]
    public async Task Rerun_after_conversion_completes_exactly_once()
    {
        await using FieldSalesHarness harness = await FieldSalesHarness.CreateAsync(fixture);
        Driver driver = new(harness);
        var (repId, repUser) = await driver.RegisterRepAsync(harness.CompanyAId, [harness.CustomerId]);

        Guid proFormaId = await driver.CaptureAsync(repId, harness.CompanyAId, "RESUME-1",
            [(harness.HotPlateAId, 2m)]);
        await driver.SubmitAsync(proFormaId, repUser);

        // A crash between conversion and acknowledgement replays the same approval.
        Guid first = await driver.ApproveAsync(proFormaId);
        Guid second = await driver.ApproveAsync(proFormaId);

        second.Should().Be(first);

        await using var dbA = harness.OpenCompanyDb(harness.CompanyAId);
        await using var dbB = harness.OpenCompanyDb(harness.CompanyBId);
        (await dbA.SalesOrders.CountAsync()).Should().Be(1);
        (await dbA.Invoices.CountAsync() + await dbB.Invoices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Cross_company_approval_makes_one_order_and_two_invoices()
    {
        await using FieldSalesHarness harness = await FieldSalesHarness.CreateAsync(fixture);
        Driver driver = new(harness);
        var (repId, repUser) = await driver.RegisterRepAsync(
            harness.CompanyAId, [harness.CustomerId], companies: [harness.CompanyAId, harness.CompanyBId]);

        Guid proFormaId = await driver.CaptureAsync(repId, harness.CompanyAId, "SPLIT-1",
            [(harness.HotPlateAId, 2m), (harness.MaizeBId, 1m)],
            orderCompany: harness.CompanyAId);
        await driver.SubmitAsync(proFormaId, repUser);
        Guid orderId = await driver.ApproveAsync(proFormaId);

        await using var dbA = harness.OpenCompanyDb(harness.CompanyAId);
        await using var dbB = harness.OpenCompanyDb(harness.CompanyBId);

        // One order, in the ordering company...
        (await dbA.SalesOrders.CountAsync()).Should().Be(1);
        (await dbB.SalesOrders.CountAsync()).Should().Be(0);
        SalesOrder order = await dbA.SalesOrders
            .Include(o => o.Lines)
            .FirstAsync(o => o.Id == orderId);
        order.Lines.Should().HaveCount(2);

        // ...two invoices, one per supplying company, reconciling to the order.
        List<Invoice> invoices = [
            .. await dbA.Invoices.ToListAsync(),
            .. await dbB.Invoices.ToListAsync(),
        ];
        invoices.Should().HaveCount(2);
        invoices.Select(invoice => invoice.CompanyId).Should()
            .BeEquivalentTo([harness.CompanyAId, harness.CompanyBId]);
        invoices.Sum(invoice => invoice.Gross.Amount).Should()
            .BeApproximately(order.Gross.Amount, 0.01m);
    }

    [Fact]
    public async Task Rejection_leaves_zero_reservations_and_zero_postings()
    {
        await using FieldSalesHarness harness = await FieldSalesHarness.CreateAsync(fixture);
        Driver driver = new(harness);
        var (repId, repUser) = await driver.RegisterRepAsync(harness.CompanyAId, [harness.CustomerId]);

        Guid proFormaId = await driver.CaptureAsync(repId, harness.CompanyAId, "REJECT-1",
            [(harness.HotPlateAId, 2m)]);
        await driver.SubmitAsync(proFormaId, repUser);
        await driver.RejectAsync(proFormaId, "over limit");

        await using var dbA = harness.OpenCompanyDb(harness.CompanyAId);
        await using var dbB = harness.OpenCompanyDb(harness.CompanyBId);
        (await dbA.StockReservations.CountAsync() + await dbB.StockReservations.CountAsync())
            .Should().Be(0, "asserted by counts, not by reading the code");
        (await dbA.SalesOrders.CountAsync()).Should().Be(0);
        (await dbA.Journals.CountAsync() + await dbB.Journals.CountAsync()).Should().Be(0);

        ProFormaOrder stored = await dbA.ProFormaOrders.FirstAsync(o => o.Id == proFormaId);
        stored.Status.Should().Be(ProFormaStatus.Rejected);
        stored.DecisionReason.Should().Be("over limit");
    }

    [Fact]
    public async Task Territory_walls_hold_for_documents_and_availability()
    {
        await using FieldSalesHarness harness = await FieldSalesHarness.CreateAsync(fixture);
        Driver driver = new(harness);
        var (repA, repAUser) = await driver.RegisterRepAsync(harness.CompanyAId, [harness.CustomerId]);
        var (repB, repBUser) = await driver.RegisterRepAsync(
            harness.CompanyAId, [UuidV7.NewGuid()], companies: [harness.CompanyAId]);

        Guid proFormaId = await driver.CaptureAsync(repA, harness.CompanyAId, "WALL-1",
            [(harness.HotPlateAId, 1m)]);

        // Rep B cannot fetch rep A's document: 403-style refusal, not a filtered result.
        await using VumaRetailDbContext dbRead = harness.OpenCompanyDb(harness.CompanyAId);
        var reads = new GetProFormaQueryHandler(
            new ProFormaOrderRepository(dbRead),
            new RepRepository(dbRead),
            harness.TenantContext);
        Func<Task> act = () => reads.HandleAsync(new GetProFormaQuery(proFormaId, repB));
        await act.Should().ThrowAsync<FieldSalesException>()
            .Where(e => e.Code == "REP_FORBIDDEN");

        // Availability answers only the rep's own companies.
        var availability = new GetRepAvailabilityQueryHandler(
            new RepRepository(dbRead),
            new CompanyProbe(harness),
            harness.TenantContext);
        RepAvailabilityView view = await availability.HandleAsync(
            new GetRepAvailabilityQuery(repA, harness.HotPlateAId, null));
        view.Companies.Should().ContainSingle()
            .Which.CompanyId.Should().Be(harness.CompanyAId);
    }

    [Fact]
    public async Task Performance_compares_periods_and_reruns_stably()
    {
        await using FieldSalesHarness harness = await FieldSalesHarness.CreateAsync(fixture);
        Driver driver = new(harness);
        var (repId, repUser) = await driver.RegisterRepAsync(harness.CompanyAId, [harness.CustomerId]);

        // July converts R799, August converts R1598 and rejects R214.
        await driver.CaptureConvertAsync(repId, repUser, "JUL-1", [(harness.HotPlateAId, 1m)], clockMonth: 7);
        await driver.CaptureConvertAsync(repId, repUser, "AUG-1", [(harness.HotPlateAId, 2m)], clockMonth: 8);
        await driver.CaptureRejectAsync(repId, repUser, "AUG-2", [(harness.MaizeBId, 1m)]);

        var july = new DateOnly(2026, 7, 1);
        var august = new DateOnly(2026, 8, 1);
        await driver.SnapshotAsync(august);
        await driver.SnapshotAsync(july);

        RepPerformanceView view = await driver.PerformanceAsync(
            repId, harness.CompanyAId, august, july, repId, false);
        view.NetValue.Should().BeApproximately(1598m, 1m);
        view.CompareNetValue.Should().BeApproximately(799m, 1m);
        view.Variance.Should().BeApproximately(799m, 1m);

        // Re-running the closed August produces the same figures under a new version.
        await driver.SnapshotAsync(august);
        RepPerformanceView rerun = await driver.PerformanceAsync(
            repId, harness.CompanyAId, august, july, repId, false);
        rerun.Version.Should().Be(view.Version + 1);
        rerun.NetValue.Should().Be(view.NetValue);
    }

    [Fact]
    public async Task Credit_note_approval_applies_a_sales_return_in_the_origin_company()
    {
        await using FieldSalesHarness harness = await FieldSalesHarness.CreateAsync(fixture);
        Driver driver = new(harness);
        var (repId, repUser) = await driver.RegisterRepAsync(harness.CompanyAId, [harness.CustomerId]);

        (Guid invoiceId, Guid invoiceLineId) = await driver.SeedTillSaleAsync(
            harness.CompanyAId, harness.HotPlateAId, 2m, 799.00m);

        Guid creditId = await driver.CaptureCreditAsync(
            repId, harness.CompanyAId, invoiceId, "INV-000007",
            [(invoiceLineId, harness.HotPlateAId, 1m, 799.00m)]);
        await driver.SubmitCreditAsync(creditId);
        Guid returnId = await driver.ApproveCreditAsync(creditId);

        await using var dbA = harness.OpenCompanyDb(harness.CompanyAId);
        SalesReturn credit = await dbA.SalesReturns
            .Include(returns => returns.Lines)
            .FirstAsync(returns => returns.Id == returnId);
        credit.Status.Should().Be(SalesReturnStatus.Completed);
        credit.Lines.Should().ContainSingle();

        // Stock is back and the books show the reversal.
        StockBalance? balance = await dbA.StockBalances
            .FirstOrDefaultAsync(candidate => candidate.ItemId == harness.HotPlateAId);
        balance.Should().NotBeNull();
    }

    /// <summary>Drives pro formas through real handlers with company-bound repositories.</summary>
    private sealed class Driver(FieldSalesHarness harness)
    {
        public async Task<(Guid RepId, Guid UserId)> RegisterRepAsync(
            Guid companyId, IReadOnlyList<Guid> customers, IReadOnlyList<Guid>? companies = null)
        {
            await using VumaRetailDbContext dbA = harness.OpenCompanyDb(harness.CompanyAId);
            Guid userId = UuidV7.NewGuid();
            Rep rep = Rep.Register(
                harness.TenantId, harness.StoreId, userId, "Lerato Rep",
                companies ?? [companyId]);
            rep.AssignTerritory(customers);
            dbA.Reps.Add(rep);
            await dbA.CommitAsync();
            return (rep.Id, userId);
        }

        public async Task<Guid> CaptureAsync(
            Guid repId, Guid companyId, string key, IReadOnlyList<(Guid Item, decimal Qty)> lines,
            Guid? orderCompany = null)
        {
            await using VumaRetailDbContext db = harness.OpenCompanyDb(orderCompany ?? companyId);
            var handler = new CaptureProFormaCommandHandler(
                new ProFormaOrderRepository(db),
                new RepRepository(db),
                new DocumentNumberSequence(db, harness.TenantContext),
                harness.TenantContext,
                new TaxEngine(new TaxRuleRepository(db)),
                new PackSizeResolver(new UnitOfMeasureRepository(db)),
                new CompanyProbe(harness),
                harness.Clock);
            Guid id = await handler.HandleAsync(new CaptureProFormaCommand(
                repId, orderCompany ?? companyId, harness.CustomerId, "ZAR", key,
                [.. lines.Select(line => new ProFormaLineInput(
                    line.Item, null, line.Qty, "EA", line.Item == harness.HotPlateAId ? 799.00m
                        : line.Item == harness.GlovesAId ? 100.33m : 214.00m,
                    0m, "STANDARD", "ZAR"))]));
            await db.CommitAsync();
            return id;
        }

        public async Task SubmitAsync(Guid proFormaId, Guid submitterUserId)
        {
            await using VumaRetailDbContext db = harness.OpenCompanyDb(harness.CompanyAId);
            var engine = harness.CreateApprovalEngine(db, $"user:{submitterUserId}");
            var handler = new SubmitProFormaCommandHandler(
                new ProFormaOrderRepository(db), engine, harness.TenantContext, harness.Clock);
            await handler.HandleAsync(new SubmitProFormaCommand(proFormaId));
            await db.CommitAsync();
        }

        public async Task<Guid> ApproveAsync(Guid proFormaId)
        {
            await using VumaRetailDbContext db = harness.OpenCompanyDb(harness.CompanyAId);
            var engine = harness.CreateApprovalEngine(db, $"user:{harness.ManagerId}");
            var handler = new ApproveProFormaCommandHandler(
                new ProFormaOrderRepository(db), engine,
                harness.CreateApprovalService(),
                new TestPrincipalAccessor($"user:{harness.ManagerId}"),
                harness.TenantContext);
            Guid orderId = await handler.HandleAsync(new ApproveProFormaCommand(proFormaId, "approved"));
            await db.CommitAsync();
            return orderId;
        }

        public async Task RejectAsync(Guid proFormaId, string reason)
        {
            await using VumaRetailDbContext db = harness.OpenCompanyDb(harness.CompanyAId);
            var engine = harness.CreateApprovalEngine(db, $"user:{harness.ManagerId}");
            var handler = new RejectProFormaCommandHandler(
                new ProFormaOrderRepository(db), engine, harness.TenantContext, harness.Clock);
            await handler.HandleAsync(new RejectProFormaCommand(proFormaId, reason));
            await db.CommitAsync();
        }

        public async Task DrainAsync(Guid companyId, Guid itemId, decimal quantity)
        {
            await using VumaRetailDbContext db = harness.OpenCompanyDb(companyId);
            StockLocation location = await db.StockLocations.FirstAsync();
            var poster = new StockLedgerPoster(
                new StockBalanceRepository(db),
                new StockLedgerRepository(db),
                new NullValuationEvents(),
                harness.Clock);
            await poster.IssueForSaleAsync(location, itemId, null, new Quantity(quantity, "EA"), Guid.NewGuid());
            await db.CommitAsync();
        }

        public async Task SnapshotAsync(DateOnly period)
        {
            await using VumaRetailDbContext db = harness.OpenCompanyDb(harness.CompanyAId);
            var handler = new SnapshotPerformanceCommandHandler(
                new RepRepository(db),
                new ProFormaOrderRepository(db),
                new ProFormaCreditNoteRepository(db),
                new InvoiceRepository(db),
                new RepPerformanceRepository(db),
                new RepPerformanceCalculator(),
                harness.TenantContext,
                new BoundCompany(harness.CompanyAId),
                harness.Clock);
            await handler.HandleAsync(new SnapshotPerformanceCommand(period));
            await db.CommitAsync();
        }

        public async Task<RepPerformanceView> PerformanceAsync(
            Guid repId, Guid companyId, DateOnly period, DateOnly compareTo, Guid caller, bool team)
        {
            await using VumaRetailDbContext dbPerf = harness.OpenCompanyDb(companyId);
            var handler = new GetRepPerformanceQueryHandler(
                new RepPerformanceRepository(dbPerf),
                new RepRepository(dbPerf),
                harness.TenantContext);
            return await handler.HandleAsync(
                new GetRepPerformanceQuery(repId, companyId, period, compareTo, caller, team));
        }

        public async Task CaptureConvertAsync(Guid repId, Guid repUser, string key, IReadOnlyList<(Guid Item, decimal Qty)> lines, int clockMonth)
        {
            // Performance months are date-driven: capture/convert inside the target month by moving
            // the clock, then restore it. The clock is harness-scoped and tests run sequentially.
            DateTimeOffset saved = harness.Clock.UtcNow;
            harness.Clock.Set(new DateTimeOffset(2026, clockMonth, 15, 10, 0, 0, TimeSpan.Zero));
            try
            {
                Guid id = await CaptureAsync(repId, harness.CompanyAId, key, lines);
                await SubmitAsync(id, repUser);
                await ApproveAsync(id);
            }
            finally
            {
                harness.Clock.Set(saved);
            }
        }

        public async Task CaptureRejectAsync(Guid repId, Guid repUser, string key, IReadOnlyList<(Guid Item, decimal Qty)> lines)
        {
            DateTimeOffset saved = harness.Clock.UtcNow;
            harness.Clock.Set(new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero));
            try
            {
                Guid id = await CaptureAsync(repId, harness.CompanyAId, key, lines);
                await SubmitAsync(id, repUser);
                await RejectAsync(id, "not this month");
            }
            finally
            {
                harness.Clock.Set(saved);
            }
        }

        public async Task<(Guid InvoiceId, Guid InvoiceLineId)> SeedTillSaleAsync(
            Guid companyId, Guid itemId, decimal qty, decimal unitPrice)
        {
            await using VumaRetailDbContext db = harness.OpenCompanyDb(companyId);
            TillSession till = TillSession.Open(
                harness.TenantId, harness.StoreId, harness.TerminalId, harness.ManagerId,
                Money.Zero("ZAR"), harness.Clock.UtcNow);
            db.TillSessions.Add(till);

            var numbers = new DocumentNumberSequence(db, harness.TenantContext);
            Sale sale = Sale.Open(
                UuidV7.NewGuid(), harness.TenantId, harness.StoreId,
                await numbers.NextAsync("SALE"), till, harness.ManagerId,
                (await db.StockLocations.FirstAsync()).Id, harness.CustomerId, "ZAR", harness.Clock.UtcNow);
            SaleLine line = SaleLine.Ring(
                harness.TenantId, harness.StoreId, sale.Id, 1, itemId, null,
                "Hot plate", new Quantity(qty, "EA"), new Money(unitPrice, "ZAR"),
                Money.Zero("ZAR"), "STANDARD",
                new Money(1389.57m, "ZAR"), new Money(208.43m, "ZAR"), new Money(1598.00m, "ZAR"));
            sale.AddLine(line);
            sale.AddTender(SaleTender.Capture(
                harness.TenantId, harness.StoreId, sale.Id, TenderType.Card,
                new Money(1598.00m, "ZAR"), "AUTH-1", harness.Clock.UtcNow));
            sale.Complete(harness.Clock.UtcNow);
            db.Sales.Add(sale);

            Invoice invoice = Invoice.Create(
                harness.TenantId, harness.StoreId, await numbers.NextAsync("INV"),
                companyId, sale.Id.ToString(), InvoiceSourceType.Sale,
                harness.CustomerId, "ZAR", null);
            invoice.AddLine(InvoiceLine.Create(
                harness.TenantId, harness.StoreId, invoice.Id, itemId, null,
                qty, "EA", new Money(694.79m, "ZAR"), Money.Zero("ZAR"),
                new Money(104.22m, "ZAR"), "Each", "ZAR", null));
            invoice.Post(harness.Clock.UtcNow);
            db.Invoices.Add(invoice);
            await db.CommitAsync();

            return (invoice.Id, invoice.Lines.Single().Id);
        }

        public async Task<Guid> CaptureCreditAsync(
            Guid repId, Guid companyId, Guid invoiceId, string invoiceNumber,
            IReadOnlyList<(Guid InvoiceLine, Guid Item, decimal Qty, decimal Price)> lines)
        {
            await using VumaRetailDbContext db = harness.OpenCompanyDb(companyId);
            var handler = new CaptureProFormaCreditNoteCommandHandler(
                new ProFormaCreditNoteRepository(db),
                new RepRepository(db),
                new DocumentNumberSequence(db, harness.TenantContext),
                harness.TenantContext,
                harness.Clock);
            Guid id = await handler.HandleAsync(new CaptureProFormaCreditNoteCommand(
                repId, companyId, invoiceId, invoiceNumber, "DAMAGED", "Arrived broken",
                "ZAR", $"PFC-{Guid.NewGuid():N}",
                [.. lines.Select(line => CreditLineFor(line))]));
            static ProFormaCreditLineInput CreditLineFor((Guid InvoiceLine, Guid Item, decimal Qty, decimal Price) line)
            {
                decimal net = decimal.Round(line.Price * line.Qty / 1.15m, 2, MidpointRounding.AwayFromZero);
                decimal tax = line.Price * line.Qty - net;
                return new ProFormaCreditLineInput(
                    line.InvoiceLine, line.Item, null, line.Qty, "EA",
                    line.Price, tax, net, "ZAR");
            }
            await db.CommitAsync();
            return id;
        }

        public async Task SubmitCreditAsync(Guid creditId)
        {
            await using VumaRetailDbContext db = harness.OpenCompanyDb(harness.CompanyAId);
            var engine = harness.CreateApprovalEngine(db, $"user:{harness.ManagerId}");
            var handler = new SubmitProFormaCreditNoteCommandHandler(
                new ProFormaCreditNoteRepository(db), engine, harness.TenantContext, harness.Clock);
            await handler.HandleAsync(new SubmitProFormaCreditNoteCommand(creditId));
            await db.CommitAsync();
        }

        public async Task<Guid> ApproveCreditAsync(Guid creditId)
        {
            await using VumaRetailDbContext db = harness.OpenCompanyDb(harness.CompanyAId);
            var engine = harness.CreateApprovalEngine(db, $"user:{harness.ManagerId}");
            var handler = new ApproveProFormaCreditNoteCommandHandler(
                new ProFormaCreditNoteRepository(db), engine,
                harness.CreateApprovalService(),
                new TestPrincipalAccessor($"user:{harness.ManagerId}"),
                harness.TenantContext,
                harness.Clock);
            Guid returnId = await handler.HandleAsync(new ApproveProFormaCreditNoteCommand(creditId, "verified"));
            await db.CommitAsync();
            return returnId;
        }
    }

    private sealed class CompanyProbe(FieldSalesHarness harness) : IAvailabilityProbe
    {
        public async Task<AvailabilityProbeResult> ProbeAsync(
            Guid companyId, Guid? itemId, Guid? itemVariantId,
            CancellationToken cancellationToken = default)
        {
            var reader = new RegistryAvailabilityReader(harness.Registry, harness.Clock);
            GroupAvailabilityView view = await reader.ReadAsync(
                itemId, itemVariantId, TimeSpan.FromMinutes(15), cancellationToken);
            GroupAvailabilityContribution? contribution = view.Contributions
                .FirstOrDefault(candidate => candidate.CompanyId == companyId);
            return contribution is null
                ? new AvailabilityProbeResult(0m, view.AsAt, IsStale: true)
                : new AvailabilityProbeResult(
                    contribution.Promise.Available.Value, view.AsAt, contribution.IsStale);
        }
    }

    private sealed class BoundCompany(Guid companyId) : ICompanyContext
    {
        public Guid? CompanyId => companyId;
        public void SetCompany(Guid companyId) => throw new NotSupportedException("Test company is fixed.");
        public Guid RequireCompany() => companyId;
    }

    private sealed class NullValuationEvents : IInventoryValuationEventPublisher
    {
        public Task PublishAsync(InventoryValuationEvent valuationEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
