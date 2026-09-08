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
/// Handler refusal paths: unknown sessions, retired barcodes, tenant walls, replays, and every
/// coded exception the domain can raise.
/// </summary>
public sealed class TradingRefusalTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid SessionCompany = UuidV7.NewGuid();
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

    public TradingRefusalTests()
    {
        _tenant.TenantId.Returns(TenantId);
        _clock.UtcNow.Returns(Now);
        _packs.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new PackSizeSnapshot("Each"));
        _tax.CalculateAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new TaxCalculation("STANDARD", new Money(86.9565m, "ZAR"), new Money(13.0435m, "ZAR"), new Money(100.00m, "ZAR"), 0.15m));
    }

    [Fact]
    public async Task Unknown_session_is_not_found()
    {
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns((TradingSession?)null);

        var handler = new AddBasketLineCommandHandler(
            _sessions, _barcodes, _links, _tax, _packs, _tenant, _clock);
        Func<Task> act = () => handler.HandleAsync(
            new AddBasketLineCommand(SessionId, "BAR-1", 1m, "EA", 100m, "ZAR"));

        await act.Should().ThrowAsync<TradingSessionException>()
            .Where(e => e.Code == "TRADING_SESSION_NOT_FOUND");
    }

    [Fact]
    public async Task Retired_barcode_is_unroutable()
    {
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(OpenSession());
        _barcodes.ResolveAsync("OLD-1", Arg.Any<CancellationToken>())
            .Returns(new BarcodeResolution([], IsLocalFallback: false));

        var handler = new AddBasketLineCommandHandler(
            _sessions, _barcodes, _links, _tax, _packs, _tenant, _clock);
        Func<Task> act = () => handler.HandleAsync(
            new AddBasketLineCommand(SessionId, "OLD-1", 1m, "EA", 100m, "ZAR"));

        await act.Should().ThrowAsync<TradingSessionException>()
            .Where(e => e.Code == "TRADING_UNROUTABLE_BARCODE");
    }

    [Fact]
    public async Task Foreign_tenant_cannot_touch_the_session()
    {
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(OpenSession());
        _tenant.TenantId.Returns(UuidV7.NewGuid());

        var handler = new VoidBasketLineCommandHandler(_sessions, _tenant, _clock);
        Func<Task> act = () => handler.HandleAsync(new VoidBasketLineCommand(SessionId, UuidV7.NewGuid()));

        await act.Should().ThrowAsync<TradingSessionException>()
            .Where(e => e.Code == "TRADING_SESSION_WRONG_TENANT");
    }

    [Fact]
    public async Task Open_replays_on_the_same_key()
    {
        TradingSession session = OpenSession();
        _sessions.FindByIdempotencyKeyAsync("KEY-1", Arg.Any<CancellationToken>()).Returns(session);

        var handler = new OpenTradingSessionCommandHandler(
            _sessions,
            Substitute.For<IDocumentNumberSequence>(),
            _tenant,
            _clock);
        Guid id = await handler.HandleAsync(new OpenTradingSessionCommand(
            UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid(), SessionCompany, "ZAR", "KEY-1"));

        id.Should().Be(session.Id);
    }

    [Fact]
    public void Every_coded_exception_carries_its_code()
    {
        TradingSessionException.LinkRequired(Guid.NewGuid(), Guid.NewGuid()).Code
            .Should().Be("TRADING_LINK_REQUIRED");
        TradingSessionException.TenderNotCovered(1m, 2m, "ZAR").Code
            .Should().Be("TRADING_TENDER_NOT_COVERED");
        TradingSessionException.AllocationMismatch(1m, 2m, "ZAR").Code
            .Should().Be("TRADING_ALLOCATION_MISMATCH");
        TradingSessionException.IllegalTransition(TradingSessionStatus.Open, "x").Code
            .Should().Be("TRADING_ILLEGAL_TRANSITION");
        TradingSessionException.UnroutableBarcode("X").Code
            .Should().Be("TRADING_UNROUTABLE_BARCODE");
        TradingSessionException.CurrencyMismatch("ZAR", "USD").Code
            .Should().Be("TRADING_CURRENCY_MISMATCH");
        TradingSessionException.ReturnWrongCompany("A", "B").Code
            .Should().Be("TRADING_RETURN_WRONG_COMPANY");
        TradingSessionException.ReturnWrongCompany("A", "B").Message
            .Should().Contain("A").And.Contain("B");
    }

    [Fact]
    public void Validators_reject_empty_commands()
    {
        new OpenTradingSessionCommandValidator().Validate(
            new OpenTradingSessionCommand(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, "", "")).IsValid
            .Should().BeFalse();
        new AddBasketLineCommandValidator().Validate(
            new AddBasketLineCommand(Guid.Empty, "", 0m, "", -1m, "")).IsValid
            .Should().BeFalse();
        new CaptureTenderCommandValidator().Validate(
            new CaptureTenderCommand(Guid.Empty, "", 0m, "")).IsValid
            .Should().BeFalse();
        new OverrideTenderAllocationCommandValidator().Validate(
            new OverrideTenderAllocationCommand(Guid.Empty, [])).IsValid
            .Should().BeFalse();
        new VoidBasketLineCommandValidator().Validate(
            new VoidBasketLineCommand(Guid.Empty, Guid.Empty)).IsValid
            .Should().BeFalse();
        new VoidTradingSessionCommandValidator().Validate(
            new VoidTradingSessionCommand(Guid.Empty, "")).IsValid
            .Should().BeFalse();
        new CompleteTradingSessionCommandValidator().Validate(
            new CompleteTradingSessionCommand(Guid.Empty)).IsValid
            .Should().BeFalse();
    }

    private static TradingSession OpenSession() => TradingSession.Open(
        SessionId, TenantId, SessionCompany, "TS-000001", UuidV7.NewGuid(), UuidV7.NewGuid(),
        UuidV7.NewGuid(), "ZAR", "KEY-1", Now);
}
