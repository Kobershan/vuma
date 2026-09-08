using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Registry.Trading;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry.Trading;

namespace VumaRetail.UnitTests.Trading;

/// <summary>
/// Session reads: the view projects segments with per-company tax, and the documents query
/// refuses anything but a completed session while stating the not-a-tax-invoice sentence.
/// </summary>
public sealed class TradingQueriesTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid CompanyA = UuidV7.NewGuid();
    private static readonly Guid CompanyB = UuidV7.NewGuid();
    private static readonly Guid SessionId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly ITradingSessionRepository _sessions = Substitute.For<ITradingSessionRepository>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();

    public TradingQueriesTests()
    {
        _tenant.TenantId.Returns(TenantId);
    }

    [Fact]
    public async Task Get_maps_segments_with_per_company_tax()
    {
        TradingSession session = FilledSession();
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(session);

        var handler = new GetTradingSessionQueryHandler(_sessions, _tenant);
        TradingSessionView view = await handler.HandleAsync(new GetTradingSessionQuery(SessionId));

        view.SessionNumber.Should().Be("TS-000001");
        view.Status.Should().Be(TradingSessionStatus.Open);
        view.Segments.Should().HaveCount(2);
        view.Segments.Single(s => s.CompanyId == CompanyA).Tax.Amount.Should().Be(208.4348m);
        view.Segments.Single(s => s.CompanyId == CompanyB).Gross.Amount.Should().Be(214.00m);
        view.Gross.Amount.Should().Be(1812.00m);
        view.TenderAmount.Should().BeNull();
        view.Segments.SelectMany(s => s.Lines).Should().HaveCount(2);
    }

    [Fact]
    public async Task Get_refuses_a_foreign_tenant()
    {
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(FilledSession());
        _tenant.TenantId.Returns(UuidV7.NewGuid());

        var handler = new GetTradingSessionQueryHandler(_sessions, _tenant);
        Func<Task> act = () => handler.HandleAsync(new GetTradingSessionQuery(SessionId));

        await act.Should().ThrowAsync<TradingSessionException>()
            .Where(e => e.Code == "TRADING_SESSION_WRONG_TENANT");
    }

    [Fact]
    public async Task Documents_state_the_sentence_and_name_both_invoices()
    {
        TradingSession session = FilledSession();
        session.CaptureTender("Card", new Money(1812.00m, "ZAR"), "AUTH-1", Now);
        session.MarkCompleting();
        session.MarkCompleted(
            [(CompanyA, Guid.NewGuid(), Guid.NewGuid(), "NG-INV-004412"),
             (CompanyB, Guid.NewGuid(), Guid.NewGuid(), "SC-INV-019338")],
            Now);
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(session);

        var handler = new GetSessionDocumentsQueryHandler(_sessions, _tenant);
        BasketSummaryModel model = await handler.HandleAsync(new GetSessionDocumentsQuery(SessionId));

        model.NotATaxInvoiceStatement.Should().Be(
            "This is not a tax invoice. Your tax invoices are NG-INV-004412 and SC-INV-019338.");
        model.Invoices.Should().HaveCount(2);
        model.Total.Amount.Should().Be(1812.00m);
    }

    [Fact]
    public async Task Documents_refuse_an_uncompleted_session()
    {
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(FilledSession());

        var handler = new GetSessionDocumentsQueryHandler(_sessions, _tenant);
        Func<Task> act = () => handler.HandleAsync(new GetSessionDocumentsQuery(SessionId));

        await act.Should().ThrowAsync<TradingSessionException>()
            .Where(e => e.Code == "TRADING_ILLEGAL_TRANSITION");
    }

    [Fact]
    public async Task Complete_delegates_and_maps()
    {
        IMixedBasketCompletionService completion = Substitute.For<IMixedBasketCompletionService>();
        Guid saleId = UuidV7.NewGuid();
        Guid invoiceId = UuidV7.NewGuid();
        completion.CompleteAsync(SessionId, Arg.Any<CancellationToken>()).Returns(
            (IReadOnlyList<CompletedSegment>)[new CompletedSegment(CompanyA, saleId, invoiceId, "INV-7")]);

        var handler = new CompleteTradingSessionCommandHandler(completion);
        IReadOnlyList<CompletedSegmentResult> result =
            await handler.HandleAsync(new CompleteTradingSessionCommand(SessionId));

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new CompletedSegmentResult(CompanyA, saleId, invoiceId, "INV-7"));
    }

    [Fact]
    public async Task Void_session_transitions_and_void_line_reports_missing()
    {
        TradingSession session = FilledSession();
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(session);

        var voidSession = new VoidTradingSessionCommandHandler(_sessions, _tenant, _clock);
        await voidSession.HandleAsync(new VoidTradingSessionCommand(SessionId, "no stock"));

        session.Status.Should().Be(TradingSessionStatus.Voided);
        session.VoidReason.Should().Be("no stock");
        await _sessions.Received(1).CommitSessionAsync(Arg.Any<CancellationToken>());

        var voidLine = new VoidBasketLineCommandHandler(_sessions, _tenant, _clock);
        Func<Task> act = () => voidLine.HandleAsync(new VoidBasketLineCommand(SessionId, UuidV7.NewGuid()));
        await act.Should().ThrowAsync<TradingSessionException>();
    }

    private readonly IClock _clock = Substitute.For<IClock>();

    private static TradingSession FilledSession()
    {
        TradingSession session = TradingSession.Open(
            SessionId, TenantId, CompanyA, "TS-000001", UuidV7.NewGuid(), UuidV7.NewGuid(),
            UuidV7.NewGuid(), "ZAR", "KEY-1", Now);
        session.AddLine(UuidV7.NewGuid(), CompanyA, "A-1", UuidV7.NewGuid(), null, "Hot plate",
            2m, "EA", new Money(799.00m, "ZAR"), Money.Zero("ZAR"), "STANDARD",
            new Money(208.4348m, "ZAR"), new Money(1389.5652m, "ZAR"), "Each", null, Now);
        session.AddLine(UuidV7.NewGuid(), CompanyB, "B-1", UuidV7.NewGuid(), null, "Maize",
            1m, "EA", new Money(214.00m, "ZAR"), Money.Zero("ZAR"), "STANDARD",
            new Money(27.9130m, "ZAR"), new Money(186.0870m, "ZAR"), "Bag", null, Now);
        return session;
    }
}
