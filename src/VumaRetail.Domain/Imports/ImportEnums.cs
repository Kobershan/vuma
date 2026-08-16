namespace VumaRetail.Domain.Imports;

/// <summary>
/// What a batch of imported rows is aimed at — the business entity the rows become.
/// </summary>
/// <remarks>
/// <para>
/// R5 names four things a shop must be able to ingest: suppliers, customers, inventory and specials.
/// These are those four, with inventory split into the two questions a shop actually asks separately
/// — "what do I sell" (<see cref="Items"/>) and "how many have I got" (<see cref="StockOnHand"/>) —
/// because they arrive in different files, from different people, at different times.
/// </para>
/// <para>
/// <see cref="Suppliers"/> and <see cref="Customers"/> both produce a Stage 06 <c>Partner</c>. They
/// are two target kinds rather than one because the file is different: a supplier sheet carries
/// payment terms and a customer sheet carries a credit limit, and asking one field catalogue to
/// describe both would make every field optional, which is how a required field stops being checked.
/// </para>
/// </remarks>
public enum ImportTargetKind
{
    /// <summary>Suppliers — a Stage 06 partner with the supplier role.</summary>
    Suppliers = 0,

    /// <summary>Customers — a Stage 06 partner with the customer role.</summary>
    Customers = 1,

    /// <summary>Items and their barcodes — the Stage 06 catalogue.</summary>
    Items = 2,

    /// <summary>Opening or counted stock — a Stage 08 ledger adjustment, never a column write.</summary>
    StockOnHand = 3,

    /// <summary>Prices onto a Stage 10 price list — what a shop means by "the specials sheet".</summary>
    PriceListLines = 4,
}

/// <summary>The file format a batch was uploaded as.</summary>
public enum ImportSourceFormat
{
    /// <summary>Comma- or delimiter-separated text, read by the RFC 4180 reader (ADR-077).</summary>
    Csv = 0,

    /// <summary>An OOXML workbook, read by ClosedXML.</summary>
    Excel = 1,

    /// <summary>A machine-generated PDF with a text layer, read by PdfPig.</summary>
    Pdf = 2,
}

/// <summary>
/// Where a batch stands in the pipeline. The transitions are the whole safety model.
/// </summary>
/// <remarks>
/// Nothing outside the <c>imports</c> schema is written before <see cref="Committed"/>. That is what
/// makes <see cref="Validated"/> a preview somebody can trust rather than a report on damage already
/// done.
/// </remarks>
public enum ImportBatchStatus
{
    /// <summary>The bytes are stored and hashed; nothing has been read out of them yet.</summary>
    Uploaded = 0,

    /// <summary>The reader has produced headers and raw rows.</summary>
    Parsed = 1,

    /// <summary>Every required target field is bound to a source column.</summary>
    Mapped = 2,

    /// <summary>Every row has been checked and carries its own verdict. This is the preview.</summary>
    Validated = 3,

    /// <summary>The valid rows have been applied to their target module, in one transaction.</summary>
    Committed = 4,

    /// <summary>Everything this batch did has been compensated (ADR-076).</summary>
    RolledBack = 5,

    /// <summary>Abandoned before commit. Nothing outside <c>imports</c> was ever touched.</summary>
    Discarded = 6,
}

/// <summary>What to do with a source row that matches something that already exists.</summary>
/// <remarks>
/// Declared on the batch before commit rather than decided per row at commit time, because the
/// decision is a policy the person importing holds ("this is a top-up, not a re-load") and a policy
/// stated up front is one the preview can show the consequences of.
/// </remarks>
public enum ImportDuplicateStrategy
{
    /// <summary>Leave the existing record alone and count the row as skipped. The safe default.</summary>
    Skip = 0,

    /// <summary>Update the existing record, keeping a before-image so rollback can restore it.</summary>
    Update = 1,

    /// <summary>Treat a duplicate as a row-level validation error, so the preview shows every clash.</summary>
    Fail = 2,
}

/// <summary>Where one source row stands.</summary>
public enum ImportRowStatus
{
    /// <summary>Parsed out of the file, not yet checked.</summary>
    Pending = 0,

    /// <summary>Checked, and it would apply cleanly.</summary>
    Valid = 1,

    /// <summary>Checked, and it would not. <see cref="ImportRow.Errors"/> says why.</summary>
    Invalid = 2,

    /// <summary>Applied to the target module.</summary>
    Committed = 3,

    /// <summary>Deliberately not applied — a duplicate under <see cref="ImportDuplicateStrategy.Skip"/>.</summary>
    Skipped = 4,

    /// <summary>Applied, then compensated by a rollback.</summary>
    RolledBack = 5,
}

/// <summary>What a committed row did to the target module.</summary>
/// <remarks>
/// This is what rollback branches on: a <see cref="Created"/> row is soft-deleted, an
/// <see cref="Updated"/> row is restored from its before-image, and a <see cref="Movement"/> row is
/// reversed by a new ledger entry because §7 rule 6 forbids editing the old one.
/// </remarks>
public enum ImportRowOutcome
{
    /// <summary>Nothing was written — the row was invalid or skipped.</summary>
    None = 0,

    /// <summary>A new entity was created. Compensated by a soft delete (§7 rule 8).</summary>
    Created = 1,

    /// <summary>An existing entity was changed. Compensated from <see cref="ImportRow.BeforeImage"/>.</summary>
    Updated = 2,

    /// <summary>
    /// An append-only movement was posted — a stock ledger entry. Compensated by a reversing entry,
    /// never by removing the original (§7 rule 6).
    /// </summary>
    Movement = 3,
}

/// <summary>The type a target field's cell is parsed into during validation.</summary>
/// <remarks>
/// Parsing happens once, at validation, and the result is stored on the row
/// (<c>CONVENTIONS.md</c> §6). A target handler that re-parses a string at commit time is how a
/// preview and a commit come to disagree.
/// </remarks>
public enum ImportFieldType
{
    /// <summary>Free text.</summary>
    Text = 0,

    /// <summary>A whole number.</summary>
    Integer = 1,

    /// <summary>A decimal that is not money — a quantity, a percentage, a factor.</summary>
    Decimal = 2,

    /// <summary>An amount, parsed into <c>Money</c> with the batch's or the row's currency.</summary>
    Money = 3,

    /// <summary>A quantity, parsed into <c>Quantity</c> with the row's unit of measure.</summary>
    Quantity = 4,

    /// <summary>A calendar date, with no time and no zone.</summary>
    Date = 5,

    /// <summary>A yes/no cell, tolerant of <c>Y</c>, <c>true</c>, <c>1</c> and their opposites.</summary>
    Boolean = 6,
}
