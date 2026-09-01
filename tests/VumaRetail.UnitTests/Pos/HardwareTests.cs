using System.Text;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;
using VumaRetail.Hardware.Receipts;
using VumaRetail.Hardware.Scanning;

namespace VumaRetail.UnitTests.Pos;

/// <summary>
/// The receipt layout and the ESC/POS byte stream. Pure functions with no device in the room, which
/// is why they can be asserted on a Linux machine for a printer that only exists on a shop counter.
/// </summary>
public sealed class ReceiptRendererTests
{
    private static readonly DateTimeOffset Completed = new(2026, 8, 15, 14, 32, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static ReceiptDocument Receipt(bool isReprint = false, decimal change = 85m)
        => new(
            "SALE-000042",
            "Harness Sandton",
            "12 Rivonia Road, Sandton, 2196",
            "4123456789",
            Completed,
            UuidV7.NewGuid(),
            "Thandi Nkosi",
            [
                new ReceiptLine(
                    "Full cream milk 2L",
                    new Quantity(1m, "EA"),
                    new Money(34.99m, "ZAR"),
                    Money.Zero("ZAR"),
                    new Money(34.99m, "ZAR"),
                    "STANDARD"),
                new ReceiptLine(
                    "Beef mince",
                    new Quantity(0.752m, "KG"),
                    new Money(89.99m, "ZAR"),
                    new Money(5m, "ZAR"),
                    new Money(62.67m, "ZAR"),
                    "STANDARD"),
            ],
            [new ReceiptTaxLine("STANDARD", new Money(84.92m, "ZAR"), new Money(12.74m, "ZAR"))],
            new Money(84.92m, "ZAR"),
            new Money(12.74m, "ZAR"),
            new Money(97.66m, "ZAR"),
            [new ReceiptTender(TenderType.Cash, new Money(97.66m + change, "ZAR"), null)],
            new Money(change, "ZAR"),
            isReprint,
            "Thank you for shopping with us");

    [Fact]
    public void Every_line_fits_the_paper()
    {
        // An 80mm printer wraps at 42 characters. A layout that overflows does not error — it silently
        // pushes the price onto the next line, which is how a customer ends up unable to read a total.
        IReadOnlyList<string> lines = ReceiptRenderer.RenderLines(Receipt(), Utc);

        lines.Should().OnlyContain(line => line.Length <= ReceiptRenderer.LineWidth);
    }

    [Fact]
    public void The_total_the_customer_pays_is_on_the_slip()
    {
        string text = ReceiptRenderer.RenderText(Receipt(), Utc);

        text.Should().Contain("TOTAL");
        text.Should().Contain("97.66");
        text.Should().Contain("Change");
        text.Should().Contain("85.00");
    }

    [Fact]
    public void The_tax_is_broken_out_per_rate_because_a_VAT_invoice_must_show_it()
    {
        string text = ReceiptRenderer.RenderText(Receipt(), Utc);

        text.Should().Contain("VAT no. 4123456789");
        text.Should().Contain("STANDARD tax");
        text.Should().Contain("12.74");
    }

    [Fact]
    public void A_weighed_line_shows_the_weight_and_the_unit_price()
    {
        string text = ReceiptRenderer.RenderText(Receipt(), Utc);

        text.Should().Contain("0.752 KG @ 89.99");
    }

    [Fact]
    public void A_single_unit_line_does_not_print_a_redundant_quantity()
    {
        // "1 x 34.99" against every item makes a receipt harder to read, not easier.
        string text = ReceiptRenderer.RenderText(Receipt(), Utc);

        text.Should().NotContain("1 EA @");
    }

    [Fact]
    public void A_discount_appears_as_its_own_line()
    {
        string text = ReceiptRenderer.RenderText(Receipt(), Utc);

        text.Should().Contain("Discount");
        text.Should().Contain("-5.00");
    }

    [Fact]
    public void No_change_line_is_printed_when_there_is_no_change()
    {
        string text = ReceiptRenderer.RenderText(Receipt(change: 0m), Utc);

        text.Should().NotContain("Change");
    }

    [Fact]
    public void A_reprint_says_so_prominently()
    {
        string original = ReceiptRenderer.RenderText(Receipt(), Utc);
        string reprint = ReceiptRenderer.RenderText(Receipt(isReprint: true), Utc);

        original.Should().NotContain("REPRINT");
        reprint.Should().Contain("*** REPRINT ***");
    }

    [Fact]
    public void The_byte_stream_initialises_the_printer_and_ends_in_a_cut()
    {
        byte[] bytes = ReceiptRenderer.RenderEscPos(Receipt(), Utc);

        bytes.Take(2).Should().Equal(EscPos.Initialise.ToArray());
        bytes.TakeLast(4).Should().Equal(EscPos.PartialCut.ToArray());
    }

    [Fact]
    public void The_drawer_is_only_kicked_when_it_is_asked_for()
    {
        // A card sale should not spring the drawer open in front of the customer.
        byte[] withoutKick = ReceiptRenderer.RenderEscPos(Receipt(), Utc);
        byte[] withKick = ReceiptRenderer.RenderEscPos(Receipt(), Utc, openDrawer: true);

        Contains(withoutKick, EscPos.KickDrawer.ToArray()).Should().BeFalse();
        Contains(withKick, EscPos.KickDrawer.ToArray()).Should().BeTrue();
    }

    [Fact]
    public void Rendering_is_deterministic()
    {
        ReceiptDocument receipt = Receipt();

        ReceiptRenderer.RenderEscPos(receipt, Utc).Should().Equal(ReceiptRenderer.RenderEscPos(receipt, Utc));
    }

    [Fact]
    public void Latin1_carries_the_accents_a_product_name_actually_uses()
    {
        // No folding needed here, and folding anyway would be a regression: "Cafe" on a slip for an
        // item the shelf label calls "Café" is a support call.
        ReceiptRenderer.Transliterate("Café Latte").Should().Be("Café Latte");
        ReceiptRenderer.Transliterate("Ürsula").Should().Be("Ürsula");
    }

    [Fact]
    public void Typographic_punctuation_from_a_supplier_spreadsheet_is_folded_not_left_to_the_encoder()
    {
        // These arrive with every description pasted out of Excel. Left alone, the encoder emits a
        // byte the printer may read as a command.
        ReceiptRenderer.Transliterate("Milk — 2L").Should().Be("Milk - 2L");
        ReceiptRenderer.Transliterate("“Value” pack").Should().Be("\"Value\" pack");
        ReceiptRenderer.Transliterate("Farmer’s Choice").Should().Be("Farmer's Choice");
    }

    [Fact]
    public void Anything_Latin1_cannot_carry_at_all_becomes_a_visible_substitution()
    {
        ReceiptRenderer.Transliterate("日本").Should().Be("??");
    }

    [Fact]
    public void The_rendered_bytes_decode_back_to_the_text_that_was_laid_out()
    {
        ReceiptDocument receipt = Receipt();
        byte[] bytes = ReceiptRenderer.RenderEscPos(receipt, Utc);

        string decoded = Encoding.Latin1.GetString(bytes);

        decoded.Should().Contain("SALE-000042");
        decoded.Should().Contain("Full cream milk 2L");
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (int start = 0; start + needle.Length <= haystack.Length; start++)
        {
            if (haystack.Skip(start).Take(needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The scale labels every deli, butchery and produce counter in a South African supermarket prints.
/// </summary>
public sealed class BarcodeScanReaderTests
{
    private readonly BarcodeScanReader _reader = new();

    /// <summary>Builds a valid 13-digit label from its first twelve digits.</summary>
    private static string Label(string prefix, string itemCode, string value)
    {
        string body = prefix + itemCode + value;
        return body + BarcodeScanReader.ComputeCheckDigit(body);
    }

    [Fact]
    public void A_plain_barcode_comes_back_as_itself()
    {
        ScannedBarcode scan = _reader.Read("6001234567892");

        scan.Kind.Should().Be(ScannedBarcodeKind.Product);
        scan.LookupCode.Should().Be("6001234567892");
        scan.EmbeddedWeightKilograms.Should().BeNull();
    }

    [Fact]
    public void An_embedded_weight_label_yields_the_item_code_and_the_weight()
    {
        // 21 | 12345 | 00752 → item 12345, 752 grams.
        string label = Label("21", "12345", "00752");

        ScannedBarcode scan = _reader.Read(label);

        scan.Kind.Should().Be(ScannedBarcodeKind.EmbeddedWeight);
        scan.LookupCode.Should().Be("12345");
        scan.EmbeddedWeightKilograms.Should().Be(0.752m);
        scan.EmbeddedPrice.Should().BeNull();
    }

    [Fact]
    public void An_embedded_price_label_yields_the_item_code_and_the_price()
    {
        // 24 | 54321 | 06799 → item 54321, R67.99.
        string label = Label("24", "54321", "06799");

        ScannedBarcode scan = _reader.Read(label);

        scan.Kind.Should().Be(ScannedBarcodeKind.EmbeddedPrice);
        scan.LookupCode.Should().Be("54321");
        scan.EmbeddedPrice.Should().Be(67.99m);
        scan.EmbeddedWeightKilograms.Should().BeNull();
    }

    [Fact]
    public void A_label_with_a_bad_check_digit_is_not_believed()
    {
        // A misread digit is far more likely than a genuine 21-prefixed product code, and ringing up
        // a weight nobody weighed is worse than failing to find the product.
        string label = Label("21", "12345", "00752");
        string corrupted = label[..12] + (label[12] == '0' ? '1' : '0');

        ScannedBarcode scan = _reader.Read(corrupted);

        scan.Kind.Should().Be(ScannedBarcodeKind.Product);
        scan.EmbeddedWeightKilograms.Should().BeNull();
    }

    [Fact]
    public void A_code_that_is_not_thirteen_digits_is_a_plain_product_code()
    {
        // EAN-8, UPC-A and a supplier's own internal code are all legitimate things to have in the
        // barcode table.
        _reader.Read("12345670").Kind.Should().Be(ScannedBarcodeKind.Product);
        _reader.Read("SUPPLIER-REF-9").Kind.Should().Be(ScannedBarcodeKind.Product);
    }

    [Fact]
    public void Whitespace_a_scanner_appends_is_tolerated()
    {
        _reader.Read(" 6001234567892 ").LookupCode.Should().Be("6001234567892");
    }

    [Fact]
    public void An_empty_scan_is_a_programming_error_not_a_product()
    {
        Action reading = () => _reader.Read("   ");

        reading.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_tenant_whose_labeller_uses_different_prefixes_configures_rather_than_forks()
    {
        BarcodeScanReader reader = new(weightPrefixes: ["27"], pricePrefixes: ["28"]);

        reader.Read(Label("27", "11111", "01500")).Kind.Should().Be(ScannedBarcodeKind.EmbeddedWeight);

        // 21 is no longer a weight prefix for this tenant.
        reader.Read(Label("21", "11111", "01500")).Kind.Should().Be(ScannedBarcodeKind.Product);
    }

    [Theory]
    [InlineData("4006381333931")]
    [InlineData("9780201379624")]
    public void Known_good_check_digits_verify(string code)
        => BarcodeScanReader.HasValidCheckDigit(code).Should().BeTrue();

    [Fact]
    public void A_code_whose_check_digit_is_wrong_does_not_verify()
        => BarcodeScanReader.HasValidCheckDigit("4006381333930").Should().BeFalse();

    [Fact]
    public void The_check_digit_calculator_agrees_with_the_verifier()
    {
        string body = "600123456789";

        char check = BarcodeScanReader.ComputeCheckDigit(body);

        BarcodeScanReader.HasValidCheckDigit(body + check).Should().BeTrue();
    }
}
