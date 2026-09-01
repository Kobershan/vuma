using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Imports;

namespace VumaRetail.Imports.Readers;

/// <summary>
/// Picks the reader for a format out of whatever the container has registered.
/// </summary>
/// <remarks>
/// Built from the registered readers rather than from a <c>switch</c> on
/// <see cref="ImportSourceFormat"/>, so a host that has not registered the PDF reader — a cloud tier
/// that never receives uploads, say — fails with "no reader for Pdf" at the call rather than at
/// container build time, and so adding a format later is a registration rather than an edit here.
/// </remarks>
/// <param name="readers">Every registered reader.</param>
public sealed class ImportSourceReaderFactory(IEnumerable<IImportSourceReader> readers)
    : IImportSourceReaderFactory
{
    private readonly IReadOnlyDictionary<ImportSourceFormat, IImportSourceReader> _readers =
        readers.GroupBy(reader => reader.Format).ToDictionary(group => group.Key, group => group.Last());

    /// <inheritdoc />
    public IImportSourceReader For(ImportSourceFormat format)
        => _readers.TryGetValue(format, out IImportSourceReader? reader)
            ? reader
            : throw new NotSupportedException($"No import source reader is registered for {format}.");
}
