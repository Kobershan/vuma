namespace VumaRetail.Application.Imports;

/// <summary>
/// The ceilings an import runs inside, and the currency its money columns are read in.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is a limit rather than a preference, and each exists because the alternative is
/// a store server that stops trading. A commit is one transaction (business rule 3): a transaction
/// holding fifty thousand rows' worth of locks is a till that cannot ring up a sale while somebody in
/// the back office imports a catalogue, and a shop does not care whose fault that is.
/// </para>
/// <para>
/// Configuration, not constants, because the right numbers depend on the box — <c>CLAUDE.md</c> §9's
/// rule that defaults are configuration applies to operational limits as much as to locale. The
/// defaults are sized for the smallest machine this product is sold onto.
/// </para>
/// </remarks>
public sealed class ImportOptions
{
    /// <summary>The configuration section these are bound from.</summary>
    public const string SectionName = "Vuma:Imports";

    /// <summary>
    /// The largest upload accepted, in bytes. 32 MB — comfortably past a fifty-thousand-row workbook
    /// and well short of anything that would exhaust a store server's memory while it is trading.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 32L * 1024 * 1024;

    /// <summary>
    /// The most data rows one batch may carry. Above this the upload is refused with an explanation
    /// that says to split the file, rather than accepted and then failed halfway through a commit.
    /// </summary>
    public int MaxRows { get; set; } = 50_000;

    /// <summary>
    /// The largest preview page. Deliberately the same shape as <c>API_STANDARDS.md</c> §8's paging:
    /// a preview of a fifty-thousand-row file must not materialise fifty thousand rows to show
    /// somebody the first twenty-five.
    /// </summary>
    public int MaxPreviewRows { get; set; } = 200;

    /// <summary>
    /// Where uploaded bytes are kept until the batch is committed or discarded, relative to the
    /// application's base directory unless rooted.
    /// </summary>
    /// <remarks>
    /// On disk rather than in the database on purpose. A 32 MB workbook has no business in a table a
    /// list endpoint pages over, and the bytes have a different lifetime from the record of what was
    /// imported: the file can go once the batch commits, while the batch and its rows are kept
    /// forever. R10 also applies — an uploaded customer list is exactly the sort of document that
    /// must not leave the premises, and keeping it out of the replicated tables is what guarantees it.
    /// </remarks>
    public string FileDirectory { get; set; } = "import-files";

    /// <summary>
    /// True to delete an upload's bytes once its batch commits or is discarded.
    /// </summary>
    /// <remarks>
    /// On by default. The batch, its rows and their raw values are the record of what was imported,
    /// and they are kept; the file itself is only needed to re-parse a batch that has not committed.
    /// Turn it off on a machine where somebody wants the original files to hand, and accept that the
    /// directory then grows for as long as the shop keeps importing.
    /// </remarks>
    public bool DeleteFileOnCommit { get; set; } = true;
}
