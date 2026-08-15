using System.Text;

namespace VumaRetail.Hardware.Receipts;

/// <summary>
/// The ESC/POS control sequences a thermal receipt printer understands.
/// </summary>
/// <remarks>
/// <para>
/// ESC/POS is Epson's command set and is what almost every 80mm thermal till printer speaks, whatever
/// name is on the front. It is a byte protocol, not a document format: the printer is a typewriter
/// that takes escape sequences, so "bold" is two bytes sent before the text and two bytes sent after,
/// and forgetting the second pair prints the rest of the shift in bold.
/// </para>
/// <para>
/// Constants rather than magic byte arrays at each call site, so a sequence is defined once and named
/// for what it does. Every one of these is from the published command reference; the comments give the
/// mnemonic so they can be checked against it.
/// </para>
/// </remarks>
public static class EscPos
{
    /// <summary>ESC — the escape byte most commands start with.</summary>
    public const byte Escape = 0x1B;

    /// <summary>GS — the group separator byte the cut and barcode commands start with.</summary>
    public const byte GroupSeparator = 0x1D;

    /// <summary><c>ESC @</c> — reset the printer to its power-on state. Sent first, always.</summary>
    public static ReadOnlySpan<byte> Initialise => [Escape, (byte)'@'];

    /// <summary><c>ESC a 0</c> — align left.</summary>
    public static ReadOnlySpan<byte> AlignLeft => [Escape, (byte)'a', 0];

    /// <summary><c>ESC a 1</c> — align centre.</summary>
    public static ReadOnlySpan<byte> AlignCentre => [Escape, (byte)'a', 1];

    /// <summary><c>ESC a 2</c> — align right.</summary>
    public static ReadOnlySpan<byte> AlignRight => [Escape, (byte)'a', 2];

    /// <summary><c>ESC E 1</c> — emphasis on.</summary>
    public static ReadOnlySpan<byte> BoldOn => [Escape, (byte)'E', 1];

    /// <summary><c>ESC E 0</c> — emphasis off.</summary>
    public static ReadOnlySpan<byte> BoldOff => [Escape, (byte)'E', 0];

    /// <summary><c>GS ! 0x11</c> — double width and double height, for the total.</summary>
    public static ReadOnlySpan<byte> DoubleSize => [GroupSeparator, (byte)'!', 0x11];

    /// <summary><c>GS ! 0x00</c> — back to normal size.</summary>
    public static ReadOnlySpan<byte> NormalSize => [GroupSeparator, (byte)'!', 0x00];

    /// <summary><c>LF</c> — one line feed.</summary>
    public static ReadOnlySpan<byte> LineFeed => [(byte)'\n'];

    /// <summary>
    /// <c>GS V 66 3</c> — feed three lines and partial-cut the paper.
    /// </summary>
    /// <remarks>
    /// A partial cut leaves a small tab holding the slip on, so it does not fall on the floor before
    /// the cashier hands it over. The three-line feed is not decoration: the cutter sits some distance
    /// above the print head, and without it the last three lines of the receipt are cut off.
    /// </remarks>
    public static ReadOnlySpan<byte> PartialCut => [GroupSeparator, (byte)'V', 66, 3];

    /// <summary>
    /// <c>ESC p 0 25 250</c> — pulse drawer-kick pin 2, which is what opens the cash drawer.
    /// </summary>
    /// <remarks>
    /// The drawer is wired to the printer, not to the PC — the printer has a modular jack on the back
    /// and the drawer plugs into it. That is why "open the drawer" is a printer command, and why a
    /// terminal with no printer has no drawer to open. The two numbers are the on and off pulse widths
    /// in 2ms units; 50ms on and 500ms off is the value nearly every drawer solenoid is specified for.
    /// </remarks>
    public static ReadOnlySpan<byte> KickDrawer => [Escape, (byte)'p', 0, 25, 250];

    /// <summary>
    /// The encoding a receipt's text is written in — Latin-1 (ISO-8859-1), the printer's usual
    /// power-on code page and the one that covers every character a South African item description
    /// normally contains.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Single-byte, so it cannot carry anything above <c>U+00FF</c>.
    /// <see cref="ReceiptRenderer.Transliterate"/> folds what it can and substitutes the rest, rather
    /// than letting the encoder emit a byte the printer may read as a control code — a receipt
    /// printing "Milk - 2L" is better than one whose cutter fires halfway down the slip.
    /// </para>
    /// <para>
    /// Chosen over code page 437 because 437 has no accented lower-case vowels at all, and over UTF-8
    /// because an ESC/POS printer does not speak it: a multi-byte sequence is read as several
    /// single-byte characters, some of which are commands.
    /// </para>
    /// </remarks>
    public static Encoding TextEncoding { get; } = Encoding.Latin1;
}
