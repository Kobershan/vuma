using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Application.Imports;

namespace VumaRetail.Infrastructure.Persistence;

/// <summary>
/// Keeps an upload's bytes on the store server's own disk until its batch commits or is discarded.
/// </summary>
/// <remarks>
/// <para>
/// <b>Local disk rather than a database column or the backup vault, and each alternative is worse for
/// a specific reason.</b> A column puts a 32 MB workbook in a table the list endpoint pages over. The
/// S3 vault (Stage 04) sends it off the premises, and an uploaded customer list is exactly the
/// document R10 says must not leave a tenant's building for vendor purposes. The disk beside the
/// server is where the file already came from and where it can safely stay for the hour a preview
/// lives.
/// </para>
/// <para>
/// <b>Losing these bytes costs nothing that matters.</b> They exist only so a batch that has not
/// committed can be re-parsed without a second upload; the batch, its rows and their raw values are
/// the record of what was imported, and those are in the database and replicated. A restore onto a
/// fresh machine that finds the directory empty loses the ability to re-parse a preview somebody
/// abandoned before the disaster, which is the correct thing to lose.
/// </para>
/// <para>
/// The filename is the batch id and nothing else — no part of it comes from the upload. A file name
/// out of a request is attacker-controlled text, and building a path out of one is how
/// <c>../../appsettings.json</c> gets overwritten.
/// </para>
/// </remarks>
/// <param name="options">Where the files live, from <c>Vuma:Imports</c>.</param>
public sealed class FileSystemImportFileStore(ImportOptions options) : IImportFileStore
{
    /// <inheritdoc />
    public async Task StoreAsync(
        Guid batchId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Root);

        await File.WriteAllBytesAsync(PathFor(batchId), content, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<byte>?> ReadAsync(
        Guid batchId, CancellationToken cancellationToken = default)
    {
        string path = PathFor(batchId);

        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        string path = PathFor(batchId);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string Root => Path.IsPathRooted(options.FileDirectory)
        ? options.FileDirectory
        : Path.Combine(AppContext.BaseDirectory, options.FileDirectory);

    /// <summary>The path a batch's bytes live at. Built from the id alone — see the type's remarks.</summary>
    /// <param name="batchId">The batch.</param>
    private string PathFor(Guid batchId) => Path.Combine(Root, $"{batchId:D}.upload");
}
