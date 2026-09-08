using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Registry.Trading;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry.Trading;

namespace VumaRetail.UnitTests.Trading;

/// <summary>
/// Tender capture and allocation override: the tender must cover the basket, the default split
/// is proportional, and a replayed identical tender is not a second payment.
/// </summary>
public sealed class CaptureTenderHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid CompanyA = UuidV7.NewGuid();
    private static readonly Guid CompanyB = UuidV7.NewGuid();
    private static readonly Guid SessionId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly ITradingSessionRepository _sessions = Substitute.For<ITradingSessionRepository>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public CaptureTenderHandlerTests()
    {
        _tenant.TenantId.Returns(TenantId);
        _clock.UtcNow.Returns(Now);
    }

    [Fact]
    public async Task Capture_fixes_a_proportional_allocation()
    {
        TradingSession session = FilledSession();
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(session);

        var handler = new CaptureTenderCommandHandler(_sessions, _tenant, _clock);
        await handler.HandleAsync(new CaptureTenderCommand(SessionId, "Card", 2113.00m, "ZAR", "AUTH-9"));

        session.Status.Should().Be(TradingSessionStatus.Tendered);
        session.TenderType.Should().Be("Card");
        session.Segments.Select(s => s.TenderAllocation!.Value.Amount).Sum().Should().Be(2113.00m);
    }

    [Fact]
    public async Task Short_tender_is_refused_and_state_is_untouched()
    {
        TradingSession session = FilledSession();
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(session);

        var handler = new CaptureTenderCommandHandler(_sessions, _tenant, _clock);
        Func<Task> act = () => handler.HandleAsync(
            new CaptureTenderCommand(SessionId, "Card", 1800.00m, "ZAR"));

        await act.Should().ThrowAsync<TradingSessionException>()
            .Where(e => e.Code == "TRADING_TENDER_NOT_COVERED");
        session.Status.Should().Be(TradingSessionStatus.Open);
        session.TenderAmount.Should().BeNull();
    }

    [Fact]
    public async Task Identical_replay_is_not_a_second_payment()
    {
        TradingSession session = FilledSession();
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(session);

        var handler = new CaptureTenderCommandHandler(_sessions, _tenant, _clock);
        await handler.HandleAsync(new CaptureTenderCommand(SessionId, "Card", 2113.00m, "ZAR", "AUTH-9"));
        await handler.HandleAsync(new CaptureTenderCommand(SessionId, "Card", 2113.00m, "ZAR", "AUTH-9"));

        session.Status.Should().Be(TradingSessionStatus.Tendered);
        session.Segments.Select(s => s.TenderAllocation!.Value.Amount).Sum().Should().Be(2113.00m);
    }

    [Fact]
    public async Task Override_replaces_the_split_when_exact()
    {
        TradingSession session = FilledSession();
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(session);

        var capture = new CaptureTenderCommandHandler(_sessions, _tenant, _clock);
        await capture.HandleAsync(new CaptureTenderCommand(SessionId, "Card", 2113.00m, "ZAR", "AUTH-9"));

        var over = new OverrideTenderAllocationCommandHandler(_sessions, _tenant);
        await over.HandleAsync(new OverrideTenderAllocationCommand(SessionId,
            [new AllocationOverride(CompanyA, 2000.00m, "ZAR"), new AllocationOverride(CompanyB, 113.00m, "ZAR")]));

        session.Segments.Single(s => s.CompanyId == CompanyA).TenderAllocation!.Value.Amount.Should().Be(2000.00m);
        session.Segments.Single(s => s.CompanyId == CompanyB).TenderAllocation!.Value.Amount.Should().Be(113.00m);
        session.Segments.Should().OnlyContain(s => s.AllocationBasis == "cashier override, exact");
    }

    [Fact]
    public async Task Override_before_tender_is_refused()
    {
        _sessions.FindAsync(SessionId, Arg.Any<CancellationToken>()).Returns(FilledSession());

        var over = new OverrideTenderAllocationCommandHandler(_sessions, _tenant);
        Func<Task> act = () => over.HandleAsync(new OverrideTenderAllocationCommand(SessionId,
            [new AllocationOverride(CompanyA, 2000.00m, "ZAR"), new AllocationOverride(CompanyB, 113.00m, "ZAR")]));

        await act.Should().ThrowAsync<TradingSessionException>()
            .Where(e => e.Code == "TRADING_ILLEGAL_TRANSITION");
    }

    private static TradingSession FilledSession()
    {
        TradingSession session = TradingSession.Open(
            SessionId, TenantId, CompanyA, "TS-000001", UuidV7.NewGuid(), UuidV7.NewGuid(),
            UuidV7.NewGuid(), "ZAR", "KEY-1", Now);
        session.AddLine(UuidV7.NewGuid(), CompanyA, "A-1", UuidV7.NewGuid(), null, "Hot plate",
            2m, "EA", new Money(799.00m, "ZAR"), Money.Zero("ZAR"), "STANDARD",
            new Money(208.6957m, "ZAR"), new Money(1389.3043m, "ZAR"), "Each", null, Now);
        session.AddLine(UuidV7.NewGuid(), CompanyB, "B-1", UuidV7.NewGuid(), null, "Maize",
            1m, "EA", new Money(214.00m, "ZAR"), Money.Zero("ZAR"), "STANDARD",
            new Money(27.9130m, "ZAR"), new Money(186.0870m, "ZAR"), "Bag", null, Now);
        return session;
    }
}
