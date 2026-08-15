using System.Globalization;
using System.Text;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Hardware.Receipts;

/// <summary>
/// Lays a <see cref="ReceiptDocument"/> out as fixed-width text, then as the bytes a thermal printer
/// takes.
/// </summary>
/// <remarks>
/// <para>
/// Two renderers over one layout. The plain-text one is what a development printer, a test assertion
/// and an emailed receipt all use; the ESC/POS one wraps the same lines in the control sequences a
/// real printer needs. Laying out twice would guarantee the two drift, and the layout is the part
/// worth testing.
/// </para>
/// <para>
/// Everything here is deterministic and side-effect free: same document in, same bytes out, no clock,
/// no device. That is what makes a receipt testable on a Linux box for a printer that only exists on a
/// shop counter.
/// </para>
/// </remarks>
public static class ReceiptRenderer
{
    /// <summary>Characters per line on an 80mm thermal printer in its default font.</summary>
    /// <remarks>
    /// 42 is the standard for font A at 80mm. A 58mm printer is 32; that is a constructor argument
    /// away, and nothing in the layout assumes 42 beyond this constant.
    /// </remarks>
    public const int LineWidth = 42;

    /// <summary>Renders the receipt as fixed-width text, one line per element.</summary>
    /// <param name="receipt">The receipt.</param>
    /// <param name="timeZone">
    /// The store's timezone, so the slip shows the time the customer was actually standing there.
    /// Everything below the edge is UTC (§7 rule 9); this is the edge.
    /// </param>
    /// <param name="width">Characters per line. Defaults to <see cref="LineWidth"/>.</param>
    public static IReadOnlyList<string> RenderLines(
        ReceiptDocument receipt, TimeZoneInfo timeZone, int width = LineWidth)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 24);

        List<string> lines = [];

        lines.Add(Centre(receipt.StoreName.ToUpperInvariant(), width));

        if (!string.IsNullOrWhiteSpace(receipt.StoreAddress))
        {
            lines.AddRange(Wrap(receipt.StoreAddress, width).Select(line => Centre(line, width)));
        }

        if (!string.IsNullOrWhiteSpace(receipt.TaxNumber))
        {
            lines.Add(Centre($"VAT no. {receipt.TaxNumber}", width));
        }

        lines.Add(new string('-', width));

        if (receipt.IsReprint)
        {
            // Prominent and unmissable. A reprint that looks like an original is the whole problem
            // ReceiptPrint exists to make visible.
            lines.Add(Centre("*** REPRINT ***", width));
            lines.Add(new string('-', width));
        }

        DateTimeOffset local = TimeZoneInfo.ConvertTime(receipt.CompletedAt, timeZone);

        lines.Add(Columns($"Receipt {receipt.SaleNumber}", local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), width));

        if (!string.IsNullOrWhiteSpace(receipt.OperatorName))
        {
            lines.Add($"Served by {receipt.OperatorName}");
        }

        lines.Add(new string('-', width));

        foreach (ReceiptLine line in receipt.Lines)
        {
            lines.Add(Truncate(line.Description, width));

            // The quantity line is only printed when it says something a single-unit line does not.
            // A receipt with "1 x 24.99" against every item is harder to read, not easier.
            if (line.Quantity.Value != 1m)
            {
                lines.Add(Columns(
                    $"  {Trim(line.Quantity.Value)} {line.Quantity.UnitOfMeasure} @ {Amount(line.UnitPrice)}",
                    Amount(line.Gross),
                    width));
            }
            else
            {
                lines.Add(Columns(string.Empty, Amount(line.Gross), width));
            }

            if (!line.DiscountAmount.IsZero)
            {
                lines.Add(Columns("  Discount", $"-{Amount(line.DiscountAmount)}", width));
            }
        }

        lines.Add(new string('-', width));
        lines.Add(Columns("Subtotal (excl.)", Amount(receipt.Net), width));

        foreach (ReceiptTaxLine tax in receipt.TaxLines)
        {
            lines.Add(Columns($"{tax.TaxCode} tax", Amount(tax.Tax), width));
        }

        lines.Add(Columns("TOTAL", Amount(receipt.Gross), width));
        lines.Add(new string('-', width));

        foreach (ReceiptTender tender in receipt.Tenders)
        {
            string label = tender.Reference is null
                ? TenderLabel(tender.Type)
                : $"{TenderLabel(tender.Type)} {tender.Reference}";

            lines.Add(Columns(Truncate(label, width - 12), Amount(tender.Amount), width));
        }

        if (!receipt.ChangeGiven.IsZero)
        {
            lines.Add(Columns("Change", Amount(receipt.ChangeGiven), width));
        }

        if (!string.IsNullOrWhiteSpace(receipt.Footer))
        {
            lines.Add(string.Empty);
            lines.AddRange(Wrap(receipt.Footer, width).Select(line => Centre(line, width)));
        }

        return lines;
    }

    /// <summary>Renders the receipt as a single block of text, for a screen, a file or an email.</summary>
    /// <param name="receipt">The receipt.</param>
    /// <param name="timeZone">The store's timezone.</param>
    /// <param name="width">Characters per line.</param>
    public static string RenderText(ReceiptDocument receipt, TimeZoneInfo timeZone, int width = LineWidth)
        => string.Join(Environment.NewLine, RenderLines(receipt, timeZone, width));

    /// <summary>
    /// Renders the receipt as the ESC/POS byte stream a thermal printer takes, ending in a cut.
    /// </summary>
    /// <param name="receipt">The receipt.</param>
    /// <param name="timeZone">The store's timezone.</param>
    /// <param name="width">Characters per line.</param>
    /// <param name="openDrawer">
    /// Whether to append the drawer kick. True for a cash sale, false for a card one — the drawer
    /// should not spring open in front of a customer who paid by card.
    /// </param>
    public static byte[] RenderEscPos(
        ReceiptDocument receipt, TimeZoneInfo timeZone, int width = LineWidth, bool openDrawer = false)
    {
        IReadOnlyList<string> lines = RenderLines(receipt, timeZone, width);

        using MemoryStream buffer = new();

        buffer.Write(EscPos.Initialise);
        buffer.Write(EscPos.AlignLeft);

        foreach (string line in lines)
        {
            buffer.Write(EscPos.TextEncoding.GetBytes(Transliterate(line)));
            buffer.Write(EscPos.LineFeed);
        }

        if (openDrawer)
        {
            buffer.Write(EscPos.KickDrawer);
        }

        buffer.Write(EscPos.PartialCut);

        return buffer.ToArray();
    }

    /// <summary>
    /// Folds the characters Latin-1 cannot carry down to ones it can, rather than letting the encoder
    /// emit a substitution byte the printer may read as a control code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mapping is an explicit table rather than Unicode normalisation. This solution builds with
    /// <c>InvariantGlobalization</c> (see <c>Directory.Build.props</c>), under which
    /// <see cref="string.Normalize(NormalizationForm)"/> is a no-op — a decomposition-based
    /// implementation would compile, pass review, and silently do nothing on the machine it shipped
    /// to.
    /// </para>
    /// <para>
    /// The entries are the ones that actually turn up. Latin-1 already carries every accented vowel a
    /// South African product name uses, so accents are left alone; what does not survive is the
    /// typographic punctuation that arrives with every item description pasted out of a supplier's
    /// spreadsheet — curly quotes, en and em dashes, ellipses and non-breaking spaces.
    /// </para>
    /// </remarks>
    /// <param name="value">The line.</param>
    public static string Transliterate(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        StringBuilder builder = new(value.Length);

        foreach (char character in value)
        {
            builder.Append(character switch
            {
                <= 'ÿ' => character,
                '‘' or '’' or '‚' or '′' => '\'',
                '“' or '”' or '„' or '″' => '"',
                '‐' or '‑' or '‒' or '–' or '—' or '―' => '-',
                '•' => '*',
                '€' => 'E',
                _ => '?',
            });
        }

        return builder.ToString();
    }

    private static string TenderLabel(TenderType type) => type switch
    {
        TenderType.Cash => "Cash",
        TenderType.Card => "Card",
        TenderType.Voucher => "Voucher",
        TenderType.MobileMoney => "Mobile money",
        TenderType.CustomerAccount => "On account",
        _ => type.ToString(),
    };

    private static string Amount(Money money)
        => money.Amount.ToString("N2", CultureInfo.InvariantCulture);

    // A quantity is decimal(18,6) and a receipt should not print "2.000000 EA". Trailing zeros go;
    // 0.752 kg keeps its three, which is exactly what the deli scale produced.
    private static string Trim(decimal value)
        => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Centre(string value, int width)
    {
        string trimmed = Truncate(value, width);
        int padding = (width - trimmed.Length) / 2;
        return new string(' ', Math.Max(0, padding)) + trimmed;
    }

    private static string Columns(string left, string right, int width)
    {
        string trimmedRight = Truncate(right, width);
        string trimmedLeft = Truncate(left, Math.Max(0, width - trimmedRight.Length - 1));
        int gap = Math.Max(1, width - trimmedLeft.Length - trimmedRight.Length);

        return trimmedLeft + new string(' ', gap) + trimmedRight;
    }

    private static string Truncate(string value, int width)
        => value.Length <= width ? value : value[..Math.Max(0, width)];

    private static IEnumerable<string> Wrap(string value, int width)
    {
        string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        StringBuilder line = new();

        foreach (string word in words)
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word.Length > width ? word[..width] : word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }
}
