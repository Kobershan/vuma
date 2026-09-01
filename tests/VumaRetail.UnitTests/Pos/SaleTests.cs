using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Pos;

/// <summary>
/// The sale aggregate's status machine and its arithmetic — the two things that decide whether a
/// customer is charged the right amount and whether the record of it can be tampered with afterwards.
/// </summary>
public sealed class SaleTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid TerminalId = UuidV7.NewGuid();
    private static readonly Guid OperatorId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 30, 0, TimeSpan.Zero);

    private static TillSession NewSession()
        => TillSession.Open(TenantId, StoreId, TerminalId, OperatorId, new Money(500m, "ZAR"), Now);

    private static Sale NewSale(TillSession? session = null)
        => Sale.Open(
            UuidV7.NewGuid(),
            TenantId,
            StoreId,
            "SALE-000001",
            session ?? NewSession(),
            OperatorId,
            LocationId,
            customerId: null,
            "ZAR",
            Now);

    /// <summary>A line at 15% inclusive VAT: R115 gross is R100 net plus R15 tax.</summary>
    private static SaleLine Line(Sale sale, decimal gross = 115m, decimal quantity = 1m)
    {
        Money grossMoney = new(gross, "ZAR");
        Money net = (grossMoney / 1.15m).RoundToCurrencyScale();

        return SaleLine.Ring(
            sale.TenantId,
            sale.StoreId,
            sale.Id,
            sale.NextLineNumber,
            UuidV7.NewGuid(),
            null,
            "Full cream milk 2L",
            new Quantity(quantity, "EA"),
            new Money(gross / quantity, "ZAR"),
            Money.Zero("ZAR"),
            "STANDARD",
            net,
            grossMoney - net,
            grossMoney);
    }

    [Fact]
    public void A_new_sale_opens_empty_and_owes_nothing()
    {
        Sale sale = NewSale();

        sale.Status.Should().Be(SaleStatus.Open);
        sale.Gross.Amount.Should().Be(0m);
        sale.AmountTendered.Amount.Should().Be(0m);
        sale.Lines.Should().BeEmpty();
    }

    [Fact]
    public void A_sale_takes_the_terminal_from_the_session_rather_than_the_caller()
    {
        // The operator names the session, not the terminal. Letting a caller state both would let a
        // sale be attributed to a till it was not rung up on, which is the one thing the cash-up
        // depends on being true.
        TillSession session = NewSession();

        Sale sale = NewSale(session);

        sale.TerminalId.Should().Be(session.TerminalId);
        sale.TillSessionId.Should().Be(session.Id);
    }

    [Fact]
    public void A_sale_cannot_be_opened_in_a_currency_other_than_its_session()
    {
        // §4.13 — a sale independently choosing its own currency is exactly what let a foreign-currency
        // sale sit on a session until the cash-up tried to add it to the drawer and crashed. Refusing it
        // at Open time, in the domain, closes the gap even for a caller that bypasses the command layer.
        TillSession session = NewSession();

        Action opening = () => Sale.Open(
            UuidV7.NewGuid(), TenantId, StoreId, "SALE-000002", session, OperatorId, LocationId,
            customerId: null, "USD", Now);

        opening.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_CURRENCY_MISMATCH");
    }

    [Fact]
    public void A_sale_cannot_be_opened_on_a_closed_session()
    {
        TillSession session = NewSession();
        session.Close(new Money(500m, "ZAR"), Money.Zero("ZAR"), 0, OperatorId, Now);

        Action opening = () => NewSale(session);

        opening.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_TILL_SESSION_CLOSED");
    }

    [Fact]
    public void Totals_are_the_sum_of_the_live_lines()
    {
        Sale sale = NewSale();

        sale.AddLine(Line(sale, 115m));
        sale.AddLine(Line(sale, 230m));

        sale.Gross.Amount.Should().Be(345m);
        sale.Net.Amount.Should().Be(300m);
        sale.Tax.Amount.Should().Be(45m);
    }

    [Fact]
    public void A_voided_line_contributes_nothing_but_stays_on_the_record()
    {
        // Both halves matter. The customer must not be charged for it, and the fact that somebody rang
        // it up and took it off again must survive — that pattern is what shrinkage looks like.
        Sale sale = NewSale();
        sale.AddLine(Line(sale, 115m));
        SaleLine second = Line(sale, 230m);
        sale.AddLine(second);

        sale.VoidLine(second.Id, Now);

        sale.Gross.Amount.Should().Be(115m);
        sale.Lines.Should().HaveCount(2);
        sale.LiveLines.Should().ContainSingle();
        second.IsVoided.Should().BeTrue();
    }

    [Fact]
    public void Line_numbers_are_never_reused_after_a_void()
    {
        // A receipt whose line 2 is a different item on the reprint than it was on the original is
        // worse than one with a gap.
        Sale sale = NewSale();
        SaleLine first = Line(sale);
        sale.AddLine(first);

        sale.VoidLine(first.Id, Now);
        SaleLine second = Line(sale);
        sale.AddLine(second);

        second.LineNumber.Should().Be(2);
    }

    [Fact]
    public void A_sale_completes_when_it_is_covered_and_hands_back_the_difference()
    {
        Sale sale = NewSale();
        sale.AddLine(Line(sale, 115m));
        sale.AddTender(SaleTender.Capture(TenantId, StoreId, sale.Id, TenderType.Cash, new Money(200m, "ZAR"), null, Now));

        sale.Complete(Now);

        sale.Status.Should().Be(SaleStatus.Completed);
        sale.ChangeGiven.Amount.Should().Be(85m);
        sale.CompletedAt.Should().Be(Now);
    }

    [Fact]
    public void An_under_tendered_sale_does_not_complete()
    {
        Sale sale = NewSale();
        sale.AddLine(Line(sale, 115m));
        sale.AddTender(SaleTender.Capture(TenantId, StoreId, sale.Id, TenderType.Cash, new Money(100m, "ZAR"), null, Now));

        Action completing = () => sale.Complete(Now);

        completing.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_SALE_NOT_FULLY_TENDERED");
    }

    [Fact]
    public void A_sale_with_no_lines_does_not_complete_however_much_was_tendered()
    {
        Sale sale = NewSale();
        sale.AddTender(SaleTender.Capture(TenantId, StoreId, sale.Id, TenderType.Cash, new Money(100m, "ZAR"), null, Now));

        Action completing = () => sale.Complete(Now);

        completing.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_SALE_HAS_NO_LINES");
    }

    [Fact]
    public void Change_can_only_come_out_of_cash_that_was_taken()
    {
        // Overpaying by card and asking for the difference back is a cash advance. A till may not do
        // that, and the refusal has to be structural rather than a policy somebody remembers.
        Sale sale = NewSale();
        sale.AddLine(Line(sale, 115m));
        sale.AddTender(SaleTender.Capture(TenantId, StoreId, sale.Id, TenderType.Card, new Money(200m, "ZAR"), "AUTH1", Now));

        Action completing = () => sale.Complete(Now);

        completing.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_CHANGE_EXCEEDS_CASH");
    }

    [Fact]
    public void A_split_tender_that_overpays_in_cash_gives_change_from_the_cash_half()
    {
        Sale sale = NewSale();
        sale.AddLine(Line(sale, 115m));
        sale.AddTender(SaleTender.Capture(TenantId, StoreId, sale.Id, TenderType.Card, new Money(50m, "ZAR"), "AUTH1", Now));
        sale.AddTender(SaleTender.Capture(TenantId, StoreId, sale.Id, TenderType.Cash, new Money(100m, "ZAR"), null, Now));

        sale.Complete(Now);

        sale.ChangeGiven.Amount.Should().Be(35m);
    }

    [Fact]
    public void A_completed_sale_refuses_every_further_mutation()
    {
        Sale sale = NewSale();
        SaleLine line = Line(sale, 115m);
        sale.AddLine(line);
        sale.AddTender(SaleTender.Capture(TenantId, StoreId, sale.Id, TenderType.Cash, new Money(115m, "ZAR"), null, Now));
        sale.Complete(Now);

        Action adding = () => sale.AddLine(Line(sale, 10m));
        Action voidingLine = () => sale.VoidLine(line.Id, Now);
        Action tendering = () => sale.AddTender(
            SaleTender.Capture(TenantId, StoreId, sale.Id, TenderType.Cash, new Money(1m, "ZAR"), null, Now));
        Action completingAgain = () => sale.Complete(Now);

        adding.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_SALE_NOT_OPEN");
        voidingLine.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_SALE_NOT_OPEN");
        tendering.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_SALE_NOT_OPEN");
        completingAgain.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_SALE_NOT_OPEN");
    }

    [Fact]
    public void A_completed_sale_is_reversed_by_a_return_not_a_void()
    {
        Sale sale = NewSale();
        sale.AddLine(Line(sale, 115m));
        sale.AddTender(SaleTender.Capture(TenantId, StoreId, sale.Id, TenderType.Cash, new Money(115m, "ZAR"), null, Now));
        sale.Complete(Now);

        Action voiding = () => sale.Void("Customer changed their mind", Now);

        voiding.Should().Throw<PosRuleException>()
            .Which.Code.Should().Be("POS_COMPLETED_SALE_CANNOT_BE_VOIDED");
    }

    [Fact]
    public void Parking_and_resuming_round_trips_without_touching_the_totals()
    {
        Sale sale = NewSale();
        sale.AddLine(Line(sale, 115m));

        sale.Park();
        sale.Status.Should().Be(SaleStatus.Parked);

        Action addingWhileParked = () => sale.AddLine(Line(sale, 10m));
        addingWhileParked.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_SALE_NOT_OPEN");

        sale.Resume();

        sale.Status.Should().Be(SaleStatus.Open);
        sale.Gross.Amount.Should().Be(115m);
    }

    [Fact]
    public void A_sale_in_another_currency_is_refused_rather_than_converted()
    {
        Sale sale = NewSale();

        Action tendering = () => sale.AddTender(
            SaleTender.Capture(TenantId, StoreId, sale.Id, TenderType.Cash, new Money(10m, "USD"), null, Now));

        tendering.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_CURRENCY_MISMATCH");
    }

    [Fact]
    public void An_incomplete_sale_contributes_nothing_to_the_drawer()
    {
        // Tenders on an abandoned sale are not money in the till. If this returned them, a cashier
        // could inflate the expected cash by opening a sale, tendering and walking away.
        Sale sale = NewSale();
        sale.AddLine(Line(sale, 115m));
        sale.AddTender(SaleTender.Capture(TenantId, StoreId, sale.Id, TenderType.Cash, new Money(115m, "ZAR"), null, Now));

        sale.CashContribution.Amount.Should().Be(0m);

        sale.Complete(Now);

        sale.CashContribution.Amount.Should().Be(115m);
    }

    [Fact]
    public void The_cash_contribution_is_net_of_the_change_handed_back()
    {
        Sale sale = NewSale();
        sale.AddLine(Line(sale, 115m));
        sale.AddTender(SaleTender.Capture(TenantId, StoreId, sale.Id, TenderType.Cash, new Money(200m, "ZAR"), null, Now));
        sale.Complete(Now);

        // R200 in, R85 back out. R115 stayed in the drawer.
        sale.CashContribution.Amount.Should().Be(115m);
    }

    [Fact]
    public void A_card_sale_puts_nothing_in_the_drawer()
    {
        Sale sale = NewSale();
        sale.AddLine(Line(sale, 115m));
        sale.AddTender(SaleTender.Capture(TenantId, StoreId, sale.Id, TenderType.Card, new Money(115m, "ZAR"), "AUTH1", Now));
        sale.Complete(Now);

        sale.CashContribution.Amount.Should().Be(0m);
    }

    [Fact]
    public void A_void_records_why()
    {
        Sale sale = NewSale();
        sale.AddLine(Line(sale, 115m));

        sale.Void("Customer left without paying", Now);

        sale.Status.Should().Be(SaleStatus.Voided);
        sale.VoidReason.Should().Be("Customer left without paying");
        sale.VoidedAt.Should().Be(Now);
    }
}
