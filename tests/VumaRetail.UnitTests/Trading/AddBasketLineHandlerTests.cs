using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Registry.Trading;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Registry.Trading;

namespace VumaRetail.UnitTests.Trading;

/// <summary>
/// Scan-time behaviour: the company comes from the routing index and a sister company's line
/// is refused without an active <c>SharedTill</c> link — at scan time, never at payment.
/// </summary>
public sealed class AddBasketLineHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid SessionCompany = UuidV7.NewGuid();
    private static readonly Guid SisterCompany = UuidV7.NewGuid();
    private static readonly Guid SessionId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly ITradingSessionRepository _sessions = Substitute.For<ITradingSessionRepository>();
    private readonly IBarcodeResolver _barcodes = Substitute.For<IBarcodeResolver>();
    private readonly ICompanyLinkService _links = Substitute.For<ICompanyLinkService>();
    private readonly ITaxCalculator _tax = Substitute.For<ITaxCalculator>();
    private readonly IPackSizeResolver _packs = Substitute.For<IPackSizeResolver>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public AddBasketLineHandlerTests()
    {
        _tenant.TenantId.Returns(TenantId);
        _clock.UtcNow.Returns(Now);
        _packs.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new PackSizeSnapshot("Each"));
        // Shelf R100.00 inclusive: net R86.9565, tax R13.0435 (15% inclusive split).
        _tax.CalculateAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new TaxCalculation("STANDARD", new Money(86.9565m, "ZAR"), new Money(13.0435m, "ZAR"), new Money(100.00m, "ZAR"), 0.15m));
    }

    [Fact]
    public async Task Unlinked_company_line_is_refused_at_scan_time()
    {
        TradingSession session = OpenSession();
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(session);
        _barcodes.ResolveAsync("MAIZE-10KG", Arg.Any<CancellationToken>()).Returns(
            new BarcodeResolution(
                [new BarcodeCandidate(SisterCompany, "SY", ItemId, null, "MZ10", "Maize 10kg", Now)],
                IsLocalFallback: false));
        _links.RequireLink(SessionCompany, SisterCompany, CompanyLinkScope.SharedTill, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(
                new InvalidOperationException("Company has no active SharedTill link")));

        var handler = new AddBasketLineCommandHandler(
            _sessions, _barcodes, _links, _tax, _packs, _tenant, _clock);

        Func<Task> act = () => handler.HandleAsync(
            new AddBasketLineCommand(SessionId, "MAIZE-10KG", 1m, "EA", 100.00m, "ZAR"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        session.Segments.Should().BeEmpty("the basket has one segment too many otherwise");
        session.Status.Should().Be(TradingSessionStatus.Open);
    }

    [Fact]
    public async Task Same_company_line_never_touches_the_link_service()
    {
        TradingSession session = OpenSession();
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(session);
        _barcodes.ResolveAsync("PLATE-01", Arg.Any<CancellationToken>()).Returns(
            new BarcodeResolution(
                [new BarcodeCandidate(SessionCompany, "NG", ItemId, null, "HP1", "Hot plate", Now)],
                IsLocalFallback: false));

        var handler = new AddBasketLineCommandHandler(
            _sessions, _barcodes, _links, _tax, _packs, _tenant, _clock);

        Guid lineId = await handler.HandleAsync(
            new AddBasketLineCommand(SessionId, "PLATE-01", 1m, "EA", 100.00m, "ZAR"));

        await _links.DidNotReceive().RequireLink(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CompanyLinkScope>(), Arg.Any<CancellationToken>());
        session.Segments.Should().ContainSingle()
            .Which.CompanyId.Should().Be(SessionCompany);
        session.Segments.Single().Lines.Single().Id.Should().Be(lineId);
    }

    [Fact]
    public async Task Linked_sister_line_snapshots_price_tax_and_pack()
    {
        TradingSession session = OpenSession();
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(session);
        _barcodes.ResolveAsync("GLOVE-03", Arg.Any<CancellationToken>()).Returns(
            new BarcodeResolution(
                [new BarcodeCandidate(SisterCompany, "NG", ItemId, null, "GL3", "Work gloves", Now)],
                IsLocalFallback: false));

        var handler = new AddBasketLineCommandHandler(
            _sessions, _barcodes, _links, _tax, _packs, _tenant, _clock);

        Guid lineId = await handler.HandleAsync(
            new AddBasketLineCommand(SessionId, "GLOVE-03", 3m, "EA", 100.00m, "ZAR"));

        await _links.Received(1).RequireLink(
            SessionCompany, SisterCompany, CompanyLinkScope.SharedTill, Arg.Any<CancellationToken>());
        TradingSessionLine line = session.Segments.Single(s => s.CompanyId == SisterCompany)
            .Lines.Single(l => l.Id == lineId);
        line.TaxCode.Should().Be("STANDARD");
        line.PackSizeDescription.Should().Be("Each");
        line.QuantityValue.Should().Be(3m);
        session.Segments.Should().HaveCount(1, "only the sister segment exists so far");
    }

    [Fact]
    public async Task Collision_is_refused_without_guessing_an_owning_company()
    {
        TradingSession session = OpenSession();
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(session);
        _barcodes.ResolveAsync("SHARED-01", Arg.Any<CancellationToken>()).Returns(
            new BarcodeResolution(
                [new BarcodeCandidate(SessionCompany, "NG", ItemId, null, "OWN", "Own item", Now),
                 new BarcodeCandidate(SisterCompany, "SY", UuidV7.NewGuid(), null, "SIS", "Sister item", Now)],
                IsLocalFallback: false));

        var handler = new AddBasketLineCommandHandler(
            _sessions, _barcodes, _links, _tax, _packs, _tenant, _clock);

        Func<Task> act = () => handler.HandleAsync(
            new AddBasketLineCommand(SessionId, "SHARED-01", 1m, "EA", 100.00m, "ZAR"));

        TradingSessionException exception = (await act.Should().ThrowAsync<TradingSessionException>()).Which;
        exception.Code.Should().Be("TRADING_AMBIGUOUS_BARCODE");
        session.Segments.Should().BeEmpty();
        await _links.DidNotReceive().RequireLink(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CompanyLinkScope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replayed_line_id_returns_the_existing_line()
    {
        TradingSession session = OpenSession();
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(session);
        _barcodes.ResolveAsync("PLATE-01", Arg.Any<CancellationToken>()).Returns(
            new BarcodeResolution(
                [new BarcodeCandidate(SessionCompany, "NG", ItemId, null, "HP1", "Hot plate", Now)],
                IsLocalFallback: false));
        Guid lineId = UuidV7.NewGuid();

        var handler = new AddBasketLineCommandHandler(
            _sessions, _barcodes, _links, _tax, _packs, _tenant, _clock);

        Guid first = await handler.HandleAsync(
            new AddBasketLineCommand(SessionId, "PLATE-01", 1m, "EA", 100.00m, "ZAR", LineId: lineId));
        Guid second = await handler.HandleAsync(
            new AddBasketLineCommand(SessionId, "PLATE-01", 1m, "EA", 100.00m, "ZAR", LineId: lineId));

        second.Should().Be(first);
        session.Segments.Single().LiveLines.Should().ContainSingle("a replay is not a second line");
    }

    private static TradingSession OpenSession() => TradingSession.Open(
        SessionId, TenantId, SessionCompany, "TS-000001", UuidV7.NewGuid(), UuidV7.NewGuid(),
        UuidV7.NewGuid(), "ZAR", "KEY-1", Now);
}
