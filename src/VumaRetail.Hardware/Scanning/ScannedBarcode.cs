using System.Globalization;

namespace VumaRetail.Hardware.Scanning;

/// <summary>
/// What a scan turned out to be: a plain product code, or a scale label carrying an embedded weight or
/// price.
/// </summary>
/// <param name="Raw">The scan exactly as the scanner delivered it.</param>
/// <param name="LookupCode">
/// The code to look the product up by. For a plain barcode this is <paramref name="Raw"/>; for an
/// embedded-weight or embedded-price label it is the normalised item code, because the label's check
/// digit and value fields differ on every package of the same product.
/// </param>
/// <param name="Kind">Which of the three it is.</param>
/// <param name="EmbeddedWeightKilograms">The weight the label carries, for an embedded-weight label.</param>
/// <param name="EmbeddedPrice">The price the label carries, for an embedded-price label.</param>
public sealed record ScannedBarcode(
    string Raw,
    string LookupCode,
    ScannedBarcodeKind Kind,
    decimal? EmbeddedWeightKilograms,
    decimal? EmbeddedPrice);

/// <summary>What a scan turned out to be.</summary>
public enum ScannedBarcodeKind
{
    /// <summary>A plain product barcode. Look it up and ring one up.</summary>
    Product = 0,

    /// <summary>A scale label carrying a weight. The quantity comes from the label, the price from the item.</summary>
    EmbeddedWeight = 1,

    /// <summary>A scale label carrying a price. The price comes from the label, the quantity is one.</summary>
    EmbeddedPrice = 2,
}

/// <summary>
/// Reads a scan, including the in-store scale labels a supermarket's deli, butchery and produce
/// counters print.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not optional.</b> A butchery scale prints an EAN-13 whose first two digits are a
/// reserved prefix, the next five are the item's own code, and the next five are the weight or the
/// price of <em>that specific package</em>. Scan two identical trays of mince and you get two
/// different barcodes. A till that treats them as plain product codes finds neither in the catalogue,
/// which is why every South African supermarket POS has to understand this and why it is worth having
/// as a pure, tested function rather than something improvised at the terminal.
/// </para>
/// <para>
/// GS1 reserves the <c>02</c> and <c>20</c>–<c>29</c> prefixes for exactly this, and leaves the
/// meaning of the five value digits to the retailer. The convention this class implements is the
/// common South African one: <c>2</c> then a digit that selects weight or price, then the item code,
/// then the value. <see cref="Prefixes"/> is a constructor argument, so a tenant whose labeller uses a
/// different split changes configuration rather than code.
/// </para>
/// <para>
/// The check digit is verified before any of this is believed. A misread scan that happens to start
/// with <c>21</c> would otherwise ring up a weight nobody weighed.
/// </para>
/// </remarks>
/// <param name="weightPrefixes">The two-digit prefixes that mean "the value field is a weight in grams".</param>
/// <param name="pricePrefixes">The two-digit prefixes that mean "the value field is a price in cents".</param>
public sealed class BarcodeScanReader(
    IReadOnlyCollection<string>? weightPrefixes = null,
    IReadOnlyCollection<string>? pricePrefixes = null)
{
    /// <summary>The default prefixes meaning the value field is a weight in grams.</summary>
    public static IReadOnlyCollection<string> DefaultWeightPrefixes { get; } = ["21", "22", "23"];

    /// <summary>The default prefixes meaning the value field is a price in the currency's minor unit.</summary>
    public static IReadOnlyCollection<string> DefaultPricePrefixes { get; } = ["24", "25", "26", "02"];

    private readonly HashSet<string> _weightPrefixes =
        new(weightPrefixes ?? DefaultWeightPrefixes, StringComparer.Ordinal);

    private readonly HashSet<string> _pricePrefixes =
        new(pricePrefixes ?? DefaultPricePrefixes, StringComparer.Ordinal);

    /// <summary>The prefixes this reader treats as embedded-value labels, for diagnostics and tests.</summary>
    public IReadOnlyCollection<string> Prefixes => [.. _weightPrefixes, .. _pricePrefixes];

    /// <summary>Reads a scan.</summary>
    /// <param name="scan">The digits the scanner delivered. Whitespace is tolerated; anything else is not.</param>
    /// <returns>
    /// What the scan is. Anything that is not a valid embedded-value EAN-13 comes back as
    /// <see cref="ScannedBarcodeKind.Product"/> with the raw scan as its lookup code — including a
    /// UPC-A, an EAN-8 or a supplier's own internal code, all of which are legitimate things to have in
    /// the barcode table.
    /// </returns>
    public ScannedBarcode Read(string scan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scan);

        string digits = scan.Trim();

        if (digits.Length != 13 || !digits.All(char.IsAsciiDigit))
        {
            return Plain(digits);
        }

        string prefix = digits[..2];

        bool isWeight = _weightPrefixes.Contains(prefix);
        bool isPrice = _pricePrefixes.Contains(prefix);

        if (!isWeight && !isPrice)
        {
            return Plain(digits);
        }

        // Believe nothing about a label that does not check out. A single misread digit is far more
        // likely than a genuine 21-prefixed code, and ringing up a weight nobody weighed is worse than
        // failing to find the product.
        if (!HasValidCheckDigit(digits))
        {
            return Plain(digits);
        }

        string itemCode = digits.Substring(2, 5);
        string valueDigits = digits.Substring(7, 5);

        if (!int.TryParse(valueDigits, NumberStyles.None, CultureInfo.InvariantCulture, out int rawValue))
        {
            return Plain(digits);
        }

        return isWeight
            ? new ScannedBarcode(
                digits,
                itemCode,
                ScannedBarcodeKind.EmbeddedWeight,
                EmbeddedWeightKilograms: rawValue / 1000m,
                EmbeddedPrice: null)
            : new ScannedBarcode(
                digits,
                itemCode,
                ScannedBarcodeKind.EmbeddedPrice,
                EmbeddedWeightKilograms: null,
                EmbeddedPrice: rawValue / 100m);
    }

    /// <summary>
    /// Verifies an EAN-13 check digit: odd positions weigh 1, even positions weigh 3, and the total
    /// must be a multiple of ten.
    /// </summary>
    /// <param name="digits">Thirteen digits.</param>
    public static bool HasValidCheckDigit(string digits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(digits);

        if (digits.Length != 13 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        int sum = 0;

        for (int position = 0; position < 12; position++)
        {
            int value = digits[position] - '0';
            sum += position % 2 == 0 ? value : value * 3;
        }

        int expected = (10 - (sum % 10)) % 10;

        return expected == digits[12] - '0';
    }

    /// <summary>Computes the EAN-13 check digit for the first twelve digits, for building test labels.</summary>
    /// <param name="twelveDigits">The first twelve digits.</param>
    public static char ComputeCheckDigit(string twelveDigits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(twelveDigits);

        if (twelveDigits.Length != 12 || !twelveDigits.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("An EAN-13 check digit is computed from exactly twelve digits.", nameof(twelveDigits));
        }

        int sum = 0;

        for (int position = 0; position < 12; position++)
        {
            int value = twelveDigits[position] - '0';
            sum += position % 2 == 0 ? value : value * 3;
        }

        return (char)('0' + ((10 - (sum % 10)) % 10));
    }

    private static ScannedBarcode Plain(string digits)
        => new(digits, digits, ScannedBarcodeKind.Product, null, null);
}
