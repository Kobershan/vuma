using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Pos;

/// <summary>
/// A sale line's structural invariants — the arithmetic a receipt is printed from, and the two-state
/// record of what happened to the stock behind it.
/// </summary>
public sealed class SaleLineTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid SaleId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 30, 0, TimeSpan.Zero);

    private static SaleLine Ring(
        decimal quantity = 1m,
        decimal unitPrice = 115m,
        decimal discount = 0m,
        decimal? net = null,
        decimal? tax = null,
        decimal? gross = null,
        Guid? itemId = null,
        Guid? variantId = null)
    {
        decimal charged = (quantity * unitPrice) - discount;
        decimal resolvedGross = gross ?? charged;
        decimal resolvedNet = net ?? decimal.Round(resolvedGross / 1.15m, 2, MidpointRounding.AwayFromZero);
        decimal resolvedTax = tax ?? resolvedGross - resolvedNet;

        return SaleLine.Ring(
            TenantId,
            StoreId,
            SaleId,
            1,
            itemId ?? (variantId is null ? ItemId : null),
            variantId,
            "Full cream milk 2L",
            new Quantity(quantity, "EA"),
            new Money(unitPrice, "ZAR"),
            new Money(discount, "ZAR"),
            "standard",
            new Money(resolvedNet, "ZAR"),
            new Money(resolvedTax, "ZAR"),
            new Money(resolvedGross, "ZAR"));
    }

    [Fact]
    public void A_line_records_what_it_was_sold_at_and_starts_with_its_stock_unissued()
    {
        SaleLine line = Ring();

        line.Gross.Amount.Should().Be(115m);
        line.Net.Amount.Should().Be(100m);
        line.Tax.Amount.Should().Be(15m);
        line.StockIssue.Should().Be(StockIssueStatus.Pending);
        line.StockLedgerEntryId.Should().BeNull();
    }

    [Fact]
    public void The_tax_code_is_normalised_so_a_lower_case_scan_groups_with_an_upper_case_one()
    {
        // The receipt's tax summary groups on this string. "standard" and "STANDARD" appearing as two
        // rates on one slip would be a compliance defect, not a cosmetic one.
        SaleLine line = Ring();

        line.TaxCode.Should().Be("STANDARD");
    }

    [Fact]
    public void A_line_whose_parts_do_not_add_up_is_refused()
    {
        Action ringing = () => Ring(net: 100m, tax: 15m, gross: 120m);

        ringing.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_LINE_DOES_NOT_BALANCE");
    }

    [Fact]
    public void A_discount_larger_than_the_line_is_refused()
    {
        // Giving an item away at zero is legitimate. Selling it for less than nothing is not.
        Action ringing = () => Ring(quantity: 1m, unitPrice: 50m, discount: 60m);

        ringing.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_DISCOUNT_EXCEEDS_LINE");
    }

    [Fact]
    public void A_discount_that_exactly_empties_the_line_is_allowed()
    {
        SaleLine line = Ring(quantity: 1m, unitPrice: 50m, discount: 50m, net: 0m, tax: 0m, gross: 0m);

        line.Gross.Amount.Should().Be(0m);
    }

    [Fact]
    public void A_zero_or_negative_quantity_is_refused()
    {
        // A return is a Stage 10 document that references this sale, not a negative line on it.
        Action zero = () => Ring(quantity: 0m);
        Action negative = () => Ring(quantity: -1m);

        zero.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_MUST_BE_POSITIVE");
        negative.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_MUST_BE_POSITIVE");
    }

    [Fact]
    public void A_line_names_exactly_one_of_an_item_or_a_variant()
    {
        Action neither = () => SaleLine.Ring(
            TenantId, StoreId, SaleId, 1, null, null, "Thing", new Quantity(1m, "EA"),
            new Money(10m, "ZAR"), Money.Zero("ZAR"), "STANDARD",
            new Money(10m, "ZAR"), Money.Zero("ZAR"), new Money(10m, "ZAR"));

        Action both = () => SaleLine.Ring(
            TenantId, StoreId, SaleId, 1, ItemId, UuidV7.NewGuid(), "Thing", new Quantity(1m, "EA"),
            new Money(10m, "ZAR"), Money.Zero("ZAR"), "STANDARD",
            new Money(10m, "ZAR"), Money.Zero("ZAR"), new Money(10m, "ZAR"));

        neither.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_EXACTLY_ONE_ITEM_OR_VARIANT");
        both.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_EXACTLY_ONE_ITEM_OR_VARIANT");
    }

    [Fact]
    public void A_weighed_line_keeps_its_fractional_quantity()
    {
        // 0.752 kg of mince at R89.99/kg. Six decimal places exist for exactly this (§7 rule 5).
        SaleLine line = Ring(quantity: 0.752m, unitPrice: 89.99m);

        line.Quantity.Value.Should().Be(0.752m);
        line.ExtendedPrice.Amount.Should().Be(67.6725m);
    }

    [Fact]
    public void Recording_a_stock_issue_names_the_ledger_row_it_produced()
    {
        SaleLine line = Ring();
        Guid entryId = UuidV7.NewGuid();

        line.RecordStockIssued(entryId);

        line.StockIssue.Should().Be(StockIssueStatus.Posted);
        line.StockLedgerEntryId.Should().Be(entryId);
        line.StockIssueNote.Should().BeNull();
    }

    [Fact]
    public void A_refused_stock_issue_keeps_the_reason_and_names_no_ledger_row()
    {
        SaleLine line = Ring();

        line.RecordStockIssueRefused("Only 0 EA is on hand; 1 EA was requested.");

        line.StockIssue.Should().Be(StockIssueStatus.Refused);
        line.StockLedgerEntryId.Should().BeNull();
        line.StockIssueNote.Should().Contain("0 EA is on hand");
    }

    [Fact]
    public void A_long_refusal_reason_is_truncated_rather_than_overflowing_its_column()
    {
        SaleLine line = Ring();

        line.RecordStockIssueRefused(new string('x', 900));

        line.StockIssueNote!.Length.Should().Be(500);
    }

    [Fact]
    public void A_line_is_voided_once_and_only_once()
    {
        SaleLine line = Ring();
        line.Void(Now);

        Action again = () => line.Void(Now);

        again.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_LINE_ALREADY_VOIDED");
    }
}

/// <summary>The tender and the print log: two append-only records that settle disputes.</summary>
public sealed class SaleTenderAndReceiptPrintTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid SaleId = UuidV7.NewGuid();
    private static readonly Guid UserId = UuidV7.NewGuid();
    private static readonly Guid TerminalId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_zero_or_negative_tender_is_refused()
    {
        Action zero = () => SaleTender.Capture(
            TenantId, StoreId, SaleId, TenderType.Cash, Money.Zero("ZAR"), null, Now);

        zero.Should().Throw<PosRuleException>().Which.Code.Should().Be("POS_MUST_BE_POSITIVE");
    }

    [Fact]
    public void Only_cash_counts_towards_the_drawer()
    {
        SaleTender cash = SaleTender.Capture(
            TenantId, StoreId, SaleId, TenderType.Cash, new Money(50m, "ZAR"), null, Now);

        SaleTender card = SaleTender.Capture(
            TenantId, StoreId, SaleId, TenderType.Card, new Money(50m, "ZAR"), "AUTH1", Now);

        cash.IsCash.Should().BeTrue();
        card.IsCash.Should().BeFalse();
    }

    [Fact]
    public void A_first_print_needs_no_reason_and_a_reprint_does()
    {
        ReceiptPrint first = ReceiptPrint.Record(
            TenantId, StoreId, SaleId, UserId, TerminalId, isReprint: false, reason: null, Now);

        first.IsReprint.Should().BeFalse();
        first.Reason.Should().BeNull();

        Action reprintWithoutReason = () => ReceiptPrint.Record(
            TenantId, StoreId, SaleId, UserId, TerminalId, isReprint: true, reason: null, Now);

        reprintWithoutReason.Should().Throw<ReceiptReprintRequiresReasonException>();
    }
}
