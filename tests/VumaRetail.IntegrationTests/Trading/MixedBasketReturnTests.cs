using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Registry.Trading;
using VumaRetail.Domain.Registry.Trading;
using VumaRetail.Domain.Sales;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Trading;

/// <summary>
/// Stage 09b returns against real PostgreSQL: a mixed-basket return splits by origin company
/// (ADR-128), and a cross-company credit is refused with both invoice numbers named.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MixedBasketReturnTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Mixed_basket_return_credits_the_origin_company()
    {
        await using TradingBasketHarness harness = await TradingBasketHarness.CreateAsync(fixture);
        TradingBasketHarness.TradingHandlers till = harness.Handlers();

        Guid sessionId = await till.OpenAsync(new OpenTradingSessionCommand(
            harness.PremisesId, harness.TerminalId, harness.CashierId, harness.CompanyAId,
            "ZAR", $"TS-{Guid.NewGuid():N}", harness.CustomerId));
        Guid hotLine = await till.AddLineAsync(
            new AddBasketLineCommand(sessionId, "HOTPLATE-2", 2m, "EA", 799.00m, "ZAR"));
        Guid maizeLine = await till.AddLineAsync(
            new AddBasketLineCommand(sessionId, "MAIZE-10KG", 1m, "EA", 214.00m, "ZAR"));
        await till.CaptureTenderAsync(new CaptureTenderCommand(sessionId, "Card", 1812.00m, "ZAR", "AUTH-101"));

        MixedBasketCompletionService completion = harness.CreateCompletionService();
        IReadOnlyList<CompletedSegment> posted = await completion.CompleteAsync(sessionId);
        string invoiceA = posted.Single(p => p.CompanyId == harness.CompanyAId).InvoiceNumber;
        string invoiceB = posted.Single(p => p.CompanyId == harness.CompanyBId).InvoiceNumber;

        MixedBasketReturnService returns = harness.CreateReturnService();

        // One hot plate goes back to Noortgats, against Noortgats' own invoice...
        Guid returnA = await returns.ReturnLinesAsync(
            sessionId, harness.CompanyAId, [new ReturnLineRequest(hotLine, 1m)],
            invoiceA, "Customer changed their mind");

        // ...and the maize goes back to Siyaya, against Siyaya's own invoice.
        Guid returnB = await returns.ReturnLinesAsync(
            sessionId, harness.CompanyBId, [new ReturnLineRequest(maizeLine, 1m)],
            invoiceB, "Bag torn open");

        await using var dbA = harness.OpenCompanyDb(harness.CompanyAId);
        await using var dbB = harness.OpenCompanyDb(harness.CompanyBId);

        SalesReturn creditA = await dbA.SalesReturns
            .Include(credit => credit.Lines)
            .FirstAsync(credit => credit.Id == returnA);
        SalesReturn creditB = await dbB.SalesReturns
            .Include(credit => credit.Lines)
            .FirstAsync(credit => credit.Id == returnB);
        creditA.Status.Should().Be(SalesReturnStatus.Completed);
        creditB.Status.Should().Be(SalesReturnStatus.Completed);
        creditA.CompanyId.Should().Be(harness.CompanyAId);
        creditB.CompanyId.Should().Be(harness.CompanyBId);

        // Each return carries exactly the returned line, in full.
        creditA.Lines.Should().ContainSingle();
        creditB.Lines.Should().ContainSingle();

        // The till sales stand (returns are new documents, never edits — §7 rule 7).
        (await dbA.Sales.CountAsync()).Should().Be(1);
        (await dbB.Sales.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Cross_company_credit_note_is_refused()
    {
        await using TradingBasketHarness harness = await TradingBasketHarness.CreateAsync(fixture);
        TradingBasketHarness.TradingHandlers till = harness.Handlers();

        Guid sessionId = await till.OpenAsync(new OpenTradingSessionCommand(
            harness.PremisesId, harness.TerminalId, harness.CashierId, harness.CompanyAId,
            "ZAR", $"TS-{Guid.NewGuid():N}", harness.CustomerId));
        await till.AddLineAsync(new AddBasketLineCommand(sessionId, "HOTPLATE-2", 1m, "EA", 799.00m, "ZAR"));
        Guid maizeLine = await till.AddLineAsync(
            new AddBasketLineCommand(sessionId, "MAIZE-10KG", 1m, "EA", 214.00m, "ZAR"));
        await till.CaptureTenderAsync(new CaptureTenderCommand(sessionId, "Card", 1013.00m, "ZAR", "AUTH-102"));

        MixedBasketCompletionService completion = harness.CreateCompletionService();
        IReadOnlyList<CompletedSegment> posted = await completion.CompleteAsync(sessionId);
        string invoiceA = posted.Single(p => p.CompanyId == harness.CompanyAId).InvoiceNumber;
        string invoiceB = posted.Single(p => p.CompanyId == harness.CompanyBId).InvoiceNumber;

        MixedBasketReturnService returns = harness.CreateReturnService();

        // Per-company INV sequences both start at INV-000001, so naming the other company's
        // identical number cannot discriminate — the refusal is proven with a foreign number
        // instead: it is not this company's invoice, and the refusal names the company's own
        // invoice together with the named one so the cashier knows which document to use.
        Func<Task> act = () => returns.ReturnLinesAsync(
            sessionId, harness.CompanyAId, [new ReturnLineRequest(maizeLine, 1m)],
            "INV-999999", "Wrong invoice attempt");

        (await act.Should().ThrowAsync<TradingSessionException>())
            .Where(e => e.Code == "TRADING_RETURN_WRONG_COMPANY"
                && e.Message.Contains(invoiceA)
                && e.Message.Contains("INV-999999"));

        // And nothing was written anywhere for the refused attempt.
        await using var dbA = harness.OpenCompanyDb(harness.CompanyAId);
        (await dbA.SalesReturns.CountAsync()).Should().Be(0);
    }
}
