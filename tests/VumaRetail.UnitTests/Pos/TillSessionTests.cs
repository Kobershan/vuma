using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Pos;

/// <summary>
/// The cash-up. The one control a shop has over its drawer, and it only works because the expected
/// figure is derived and the counted one is entered.
/// </summary>
public sealed class TillSessionTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid TerminalId = UuidV7.NewGuid();
    private static readonly Guid OperatorId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Opened = new(2026, 8, 15, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Closed = new(2026, 8, 15, 18, 0, 0, TimeSpan.Zero);

    private static TillSession NewSession(decimal openingFloat = 500m)
        => TillSession.Open(TenantId, StoreId, TerminalId, OperatorId, new Money(openingFloat, "ZAR"), Opened);

    [Fact]
    public void An_open_session_reports_nothing_it_has_not_counted_yet()
    {
        TillSession session = NewSession();

        session.Status.Should().Be(TillSessionStatus.Open);
        session.CountedCash.Should().BeNull();
        session.ExpectedCash.Should().BeNull();
        session.Variance.Should().BeNull();
    }

    [Fact]
    public void A_balanced_drawer_closes_with_no_variance()
    {
        TillSession session = NewSession(500m);

        session.Close(new Money(1_750m, "ZAR"), new Money(1_250m, "ZAR"), 0, OperatorId, Closed);

        session.ExpectedCash!.Value.Amount.Should().Be(1_750m);
        session.CountedCash!.Value.Amount.Should().Be(1_750m);
        session.Variance!.Value.Amount.Should().Be(0m);
        session.Status.Should().Be(TillSessionStatus.Closed);
    }

    [Fact]
    public void A_short_drawer_records_a_negative_variance_rather_than_being_corrected()
    {
        // Nothing here lets the expected figure move to meet the count. A cash-up whose variance can
        // be edited to zero tells you nothing at all.
        TillSession session = NewSession(500m);

        session.Close(new Money(1_700m, "ZAR"), new Money(1_250m, "ZAR"), 0, OperatorId, Closed, "R50 short, investigating");

        session.Variance!.Value.Amount.Should().Be(-50m);
        session.Note.Should().Be("R50 short, investigating");
    }

    [Fact]
    public void An_over_drawer_records_a_positive_variance()
    {
        TillSession session = NewSession(500m);

        session.Close(new Money(1_775m, "ZAR"), new Money(1_250m, "ZAR"), 0, OperatorId, Closed);

        session.Variance!.Value.Amount.Should().Be(25m);
    }

    [Fact]
    public void The_expected_cash_includes_the_float_even_when_nothing_was_sold()
    {
        TillSession session = NewSession(500m);

        session.Close(new Money(500m, "ZAR"), Money.Zero("ZAR"), 0, OperatorId, Closed);

        session.ExpectedCash!.Value.Amount.Should().Be(500m);
        session.Variance!.Value.Amount.Should().Be(0m);
    }

    [Fact]
    public void A_session_cannot_close_while_a_sale_is_still_open_on_it()
    {
        // Counting a drawer against a total that is still moving produces a variance that means
        // nothing.
        TillSession session = NewSession();

        Action closing = () => session.Close(
            new Money(500m, "ZAR"), Money.Zero("ZAR"), openSaleCount: 2, OperatorId, Closed);

        closing.Should().Throw<PosRuleException>()
            .Which.Code.Should().Be("POS_TILL_SESSION_HAS_OPEN_SALES");
    }

    [Fact]
    public void A_closed_session_is_never_reopened_or_recounted()
    {
        TillSession session = NewSession();
        session.Close(new Money(500m, "ZAR"), Money.Zero("ZAR"), 0, OperatorId, Closed);

        Action recounting = () => session.Close(
            new Money(600m, "ZAR"), Money.Zero("ZAR"), 0, OperatorId, Closed);

        recounting.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_TILL_SESSION_CLOSED");
    }

    [Fact]
    public void A_count_in_another_currency_is_refused()
    {
        TillSession session = NewSession();

        Action closing = () => session.Close(
            new Money(500m, "USD"), Money.Zero("ZAR"), 0, OperatorId, Closed);

        closing.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_CURRENCY_MISMATCH");
    }

    [Fact]
    public void A_negative_float_is_refused()
    {
        Action opening = () => TillSession.Open(
            TenantId, StoreId, TerminalId, OperatorId, new Money(-1m, "ZAR"), Opened);

        opening.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_MUST_BE_POSITIVE");
    }
}
