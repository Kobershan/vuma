namespace VumaRetail.Imports;

/// <summary>Rejects uploads whose bytes contradict their declared source format.</summary>
public static class ImportFormatValidator
{
    private static readonly byte[] XlsxMagic = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>Returns whether the initial bytes are plausible for the declared format.</summary>
    public static bool IsValid(VumaRetail.Domain.Imports.ImportSourceFormat format, ReadOnlySpan<byte> bytes)
        => format switch
        {
            VumaRetail.Domain.Imports.ImportSourceFormat.Excel => bytes.Length >= 4 && bytes[..4].SequenceEqual(XlsxMagic),
            VumaRetail.Domain.Imports.ImportSourceFormat.Csv => IsValidText(bytes),
            _ => true,
        };

    private static bool IsValidText(ReadOnlySpan<byte> bytes)
        => !(bytes.Length >= 4 && bytes[..4].SequenceEqual(XlsxMagic))
            && !(bytes.Length >= 2 && bytes[0] == 0x25 && bytes[1] == 0x50);
}
