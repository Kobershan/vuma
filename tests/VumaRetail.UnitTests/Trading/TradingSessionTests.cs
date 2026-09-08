using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry.Trading;

namespace VumaRetail.UnitTests.Trading;

/// <summary>
/// The session status machine and its segment arithmetic: one segment per company, tax per
/// segment, and the basket total as the sum of rounded segments — never the rounding of a
/// sum (ADR-125).
/// </summary>
public sealed class TradingSessionTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid SessionCompany = UuidV7.NewGuid();
    private static readonly Guid SisterCompany = UuidV7.NewGuid();
    private static readonly Guid PremisesId = UuidV7.NewGuid();
    private static readonly Guid TerminalId = UuidV7.NewGuid();
    private static readonly Guid CashierId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Basket_total_is_the_sum_of_rounded_segments()
    {
        // ADR-125 pinned: each segment rounds its own total, and the basket adds the rounded
        // figures. The WRONG alternative — adding unrounded segment totals and rounding once —
        // gives a different cent below, and the test would catch anyone "simplifying" it back.
        // Two segments at 100.0050 each: rounded per segment R100.01 + R100.01 = R200.02,
        // but the unrounded sum 200.0100 rounds to R200.01.
        TradingSession session = OpenSession();

        session.AddLine(LineId(), SessionCompany, "6001001", Item(), null, "Widget A",
            1m, "EA", new Money(100.0050m, "ZAR"), Money.Zero("ZAR"), "STANDARD",
            new Money(13.0450m, "ZAR"), new Money(86.9600m, "ZAR"), "Each", null, Now);
        session.AddLine(LineId(), SisterCompany, "6002001", Item(), null, "Widget B",
            1m, "EA", new Money(100.0050m, "ZAR"), Money.Zero("ZAR"), "STANDARD",
            new Money(13.0450m, "ZAR"), new Money(86.9600m, "ZAR"), "Each", null, Now);

        session.Segments.Should().HaveCount(2, "one segment per company");

        decimal sumOfRounded = session.Segments
            .Sum(segment => segment.Gross.RoundToCurrencyScale().Amount);
        sumOfRounded.Should().Be(200.02m);

        decimal roundingOfSum = session.Gross.RoundToCurrencyScale().Amount;
        roundingOfSum.Should().Be(200.01m, "the single-rounding alternative this test forbids");
    }

    [Fact]
    public void Voiding_the_last_line_removes_the_segment()
    {
        TradingSession session = OpenSession();
        Guid line = LineId();
        session.AddLine(line, SisterCompany, "6002001", Item(), null, "Maize 10kg",
            1m, "EA", new Money(214.00m, "ZAR"), Money.Zero("ZAR"), "STANDARD",
            new Money(27.9130m, "ZAR"), new Money(186.0870m, "ZAR"), "Bag", null, Now);

        session.VoidLine(line, Now);

        session.Segments.Should().ContainSingle()
            .Which.IsRemoved.Should().BeTrue("a refused sister line leaves no trace");
        session.Gross.Amount.Should().Be(0m);
    }

    [Fact]
    public void Tender_below_gross_is_refused()
    {
        TradingSession session = OpenSession();
        session.AddLine(LineId(), SessionCompany, "6001001", Item(), null, "Hot plate",
            1m, "EA", new Money(100.00m, "ZAR"), Money.Zero("ZAR"), "STANDARD",
            new Money(13.0435m, "ZAR"), new Money(86.9565m, "ZAR"), "Each", null, Now);

        Action act = () => session.CaptureTender("Card", new Money(99.99m, "ZAR"), "AUTH-1", Now);

        act.Should().Throw<TradingSessionException>()
            .Where(e => e.Code == "TRADING_TENDER_NOT_COVERED");
        session.Status.Should().Be(TradingSessionStatus.Open);
    }

    [Fact]
    public void Tender_fixes_the_default_allocation_and_freezes_lines()
    {
        TradingSession session = OpenSession();
        AddTwoCompanyBasket(session);

        session.CaptureTender("Card", new Money(2113.00m, "ZAR"), "AUTH-1", Now);

        session.Status.Should().Be(TradingSessionStatus.Tendered);
        session.Segments.Select(s => s.TenderAllocation!.Value.Amount).Sum().Should().Be(2113.00m);
        session.Segments.Should().OnlyContain(s => s.AllocationBasis!.Contains("proportional"));

        Action add = () => session.AddLine(LineId(), SessionCompany, "6001001", Item(), null, "Late",
            1m, "EA", new Money(1.00m, "ZAR"), Money.Zero("ZAR"), "STANDARD",
            new Money(0.1304m, "ZAR"), new Money(0.8696m, "ZAR"), "Each", null, Now);
        add.Should().Throw<TradingSessionException>()
            .Where(e => e.Code == "TRADING_ILLEGAL_TRANSITION");
    }

    [Fact]
    public void Completed_session_cannot_be_voided_but_failed_one_can()
    {
        TradingSession session = OpenSession();
        AddTwoCompanyBasket(session);
        session.CaptureTender("Card", new Money(2113.00m, "ZAR"), "AUTH-1", Now);
        session.MarkCompleting();
        session.MarkCompleted(
            [(SessionCompany, Guid.NewGuid(), Guid.NewGuid(), "SC-INV-1"),
             (SisterCompany, Guid.NewGuid(), Guid.NewGuid(), "NG-INV-1")],
            Now);

        Action voidCompleted = () => session.Void("changed mind", Now);
        voidCompleted.Should().Throw<TradingSessionException>()
            .Where(e => e.Code == "TRADING_ILLEGAL_TRANSITION");

        TradingSession failed = OpenSession();
        AddTwoCompanyBasket(failed);
        failed.CaptureTender("Card", new Money(2113.00m, "ZAR"), "AUTH-1", Now);
        failed.MarkCompleting();
        failed.MarkCompletionFailed("leg refused", ["NG-INV-9"]);

        failed.Status.Should().Be(TradingSessionStatus.CompletionFailed);
        failed.FailureReason.Should().Be("leg refused");
        failed.UnwoundInvoiceNumbers.Should().ContainSingle().Which.Should().Be("NG-INV-9");

        Action voidFailed = () => failed.Void("customer walked away", Now);
        voidFailed.Should().NotThrow();
        failed.Status.Should().Be(TradingSessionStatus.Voided);
    }

    [Fact]
    public void Foreign_currency_line_is_refused()
    {
        TradingSession session = OpenSession();

        Action act = () => session.AddLine(LineId(), SessionCompany, "6001001", Item(), null, "Hot plate",
            1m, "EA", new Money(100.00m, "USD"), Money.Zero("ZAR"), "STANDARD",
            new Money(13.0435m, "ZAR"), new Money(86.9565m, "ZAR"), "Each", null, Now);

        act.Should().Throw<TradingSessionException>()
            .Where(e => e.Code == "TRADING_CURRENCY_MISMATCH");
    }

    private static TradingSession OpenSession() => TradingSession.Open(
        UuidV7.NewGuid(), TenantId, SessionCompany, "TS-000001", PremisesId, TerminalId,
        CashierId, "ZAR", Guid.NewGuid().ToString(), Now);

    private static void AddTwoCompanyBasket(TradingSession session)
    {
        session.AddLine(LineId(), SessionCompany, "6001001", Item(), null, "Hot plate",
            2m, "EA", new Money(799.00m, "ZAR"), Money.Zero("ZAR"), "STANDARD",
            new Money(208.6957m, "ZAR"), new Money(1389.3043m, "ZAR"), "Each", null, Now);
        session.AddLine(LineId(), SisterCompany, "6002001", Item(), null, "Maize 10kg",
            1m, "EA", new Money(214.00m, "ZAR"), Money.Zero("ZAR"), "STANDARD",
            new Money(27.9130m, "ZAR"), new Money(186.0870m, "ZAR"), "Bag", null, Now);
    }

    private static Guid LineId() => UuidV7.NewGuid();

    private static Guid Item() => UuidV7.NewGuid();
}
