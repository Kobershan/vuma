namespace VumaRetail.Contracts.Imports;

/// <summary>The id of something the <c>imports</c> module just created.</summary>
/// <param name="Id">The new row's id.</param>
public sealed record ImportsIdResponse(Guid Id);

/// <summary>
/// Uploads a file to be imported.
/// </summary>
/// <param name="TargetKind">What the rows are aimed at — <c>Suppliers</c>, <c>Customers</c>, <c>Items</c>, <c>StockOnHand</c> or <c>PriceListLines</c>.</param>
/// <param name="SourceFormat">The format — <c>Csv</c>, <c>Excel</c> or <c>Pdf</c>.</param>
/// <param name="FileName">The name the file was chosen under, for the person's benefit only.</param>
/// <param name="ContentBase64">The file's bytes, base64-encoded.</param>
/// <param name="DuplicateStrategy">
/// What to do about rows matching something that already exists — <c>Skip</c>, <c>Update</c> or
/// <c>Fail</c>. Defaults to <c>Skip</c>, the option that changes nothing it was not asked to.
/// </param>
/// <param name="StoreId">The store, required where the target is store-scoped.</param>
/// <param name="Worksheet">The worksheet to read, for a multi-sheet workbook. First sheet when omitted.</param>
/// <param name="Delimiter">The CSV delimiter, or omitted to detect it from the header row.</param>
/// <remarks>
/// Base64 in a JSON body rather than <c>multipart/form-data</c>: every other endpoint in this API is
/// JSON in and JSON out, the response to an upload is a structured object rather than a redirect, and
/// a 32 MB ceiling makes the roughly one-third encoding overhead affordable. It also keeps the
/// contract describable in OpenAPI, which is what the Android app and the eventual WPF screen
/// generate their clients from.
/// </remarks>
public sealed record CreateImportBatchRequest(
    string TargetKind,
    string SourceFormat,
    string FileName,
    string ContentBase64,
    string? DuplicateStrategy = null,
    Guid? StoreId = null,
    string? Worksheet = null,
    string? Delimiter = null);

/// <summary>What an upload produced, and whether it still needs a person.</summary>
/// <param name="BatchId">The new batch.</param>
/// <param name="BatchNumber">Its number.</param>
/// <param name="Status">Where it stands — <c>Parsed</c> if it still needs mapping, <c>Mapped</c> if not.</param>
/// <param name="SourceColumns">The headers the reader found, in file order.</param>
/// <param name="TotalRows">How many data rows it has.</param>
/// <param name="MappedAutomatically">True when it can go straight to validation.</param>
/// <param name="TemplateCode">The saved template that mapped it, when one did.</param>
/// <param name="UnmappedRequiredFields">The required fields still bound to nothing.</param>
public sealed record CreateImportBatchResponse(
    Guid BatchId,
    string BatchNumber,
    string Status,
    IReadOnlyList<string> SourceColumns,
    int TotalRows,
    bool MappedAutomatically,
    string? TemplateCode,
    IReadOnlyList<string> UnmappedRequiredFields);

/// <summary>One target field bound to one source column, or to a constant.</summary>
/// <param name="TargetField">The target field's name, from the field catalogue.</param>
/// <param name="SourceColumn">The header it reads, or <c>null</c> for a constant-only binding.</param>
/// <param name="DefaultValue">
/// The constant to use where the file has no such column, or where a cell is empty.
/// </param>
public sealed record ImportBindingContract(string TargetField, string? SourceColumn, string? DefaultValue);

/// <summary>Binds the target's fields to the file's columns.</summary>
/// <param name="Bindings">Every binding. Replaces whatever was there.</param>
/// <remarks>
/// Replaces rather than merges, and every row's verdict is discarded with the old mapping — a row
/// judged against bindings that no longer exist is a preview somebody would act on and a commit that
/// would do something else. Validate again after setting a mapping.
/// </remarks>
public sealed record SetImportMappingRequest(IReadOnlyList<ImportBindingContract> Bindings);

/// <summary>Takes back everything a committed batch did.</summary>
/// <param name="Reason">Why — required, because a rollback undoes work others may rely on.</param>
public sealed record RollbackImportBatchRequest(string Reason);

/// <summary>Saves a batch's mapping for reuse.</summary>
/// <param name="Code">The template's code, unique per tenant.</param>
/// <param name="Name">What to call it — "Makro monthly price list".</param>
public sealed record SaveImportMappingTemplateRequest(string Code, string Name);

/// <summary>A batch's six counters — the report a person reads before deciding to commit.</summary>
/// <param name="TotalRows">How many data rows the file had.</param>
/// <param name="ValidRows">How many would apply cleanly.</param>
/// <param name="InvalidRows">How many would not.</param>
/// <param name="CreatedRows">How many created something. Zero until the batch commits.</param>
/// <param name="UpdatedRows">How many changed something. Zero until the batch commits.</param>
/// <param name="SkippedRows">How many were deliberately left alone as duplicates or no-ops.</param>
public sealed record ImportBatchCountsResponse(
    int TotalRows, int ValidRows, int InvalidRows, int CreatedRows, int UpdatedRows, int SkippedRows);

/// <summary>An import batch, as returned by the API.</summary>
/// <param name="Id">The batch.</param>
/// <param name="BatchNumber">Its number.</param>
/// <param name="TargetKind">What the rows are aimed at.</param>
/// <param name="SourceFormat">The format it was uploaded as.</param>
/// <param name="FileName">The name it was uploaded under.</param>
/// <param name="ContentHash">Lower-case hex SHA-256 of the bytes — what a duplicate upload is matched on.</param>
/// <param name="SizeBytes">How big the upload was.</param>
/// <param name="Status">Where it stands in the pipeline.</param>
/// <param name="DuplicateStrategy">What it does about rows that already exist.</param>
/// <param name="StoreId">The store it is for, where the target is store-scoped.</param>
/// <param name="Worksheet">The worksheet read, for a workbook.</param>
/// <param name="SourceColumns">The headers, in file order.</param>
/// <param name="Mappings">The field bindings currently in force.</param>
/// <param name="Counts">The six counters.</param>
/// <param name="CommittedAt">When it was committed, UTC.</param>
/// <param name="CommittedBy">Who committed it.</param>
/// <param name="RolledBackAt">When it was rolled back, UTC.</param>
/// <param name="RolledBackBy">Who rolled it back.</param>
/// <param name="RollbackReason">Why it was rolled back.</param>
public sealed record ImportBatchResponse(
    Guid Id,
    string BatchNumber,
    string TargetKind,
    string SourceFormat,
    string FileName,
    string ContentHash,
    long SizeBytes,
    string Status,
    string DuplicateStrategy,
    Guid? StoreId,
    string? Worksheet,
    IReadOnlyList<string> SourceColumns,
    IReadOnlyList<ImportBindingContract> Mappings,
    ImportBatchCountsResponse Counts,
    DateTimeOffset? CommittedAt,
    string? CommittedBy,
    DateTimeOffset? RolledBackAt,
    string? RolledBackBy,
    string? RollbackReason);

/// <summary>One reason a row will not import.</summary>
/// <param name="Code">The stable machine-readable code.</param>
/// <param name="Field">The target field at fault, or <c>null</c> when the whole row is.</param>
/// <param name="Message">The human-readable explanation.</param>
public sealed record ImportRowErrorResponse(string Code, string? Field, string Message);

/// <summary>One row of the preview.</summary>
/// <param name="Id">The row.</param>
/// <param name="RowNumber">Its line number in the source, counting the header as 1.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Outcome">What the commit did to the target module.</param>
/// <param name="TargetEntityId">The entity it created, updated or moved.</param>
/// <param name="CompensationEntityId">The reversing entry a rollback wrote, for a movement.</param>
/// <param name="RawValues">Exactly what the file said, keyed by source column.</param>
/// <param name="NormalisedValues">The mapped, parsed values, keyed by target field.</param>
/// <param name="Errors">Why the row is invalid. Empty for every other status.</param>
public sealed record ImportRowResponse(
    Guid Id,
    int RowNumber,
    string Status,
    string Outcome,
    Guid? TargetEntityId,
    Guid? CompensationEntityId,
    IReadOnlyDictionary<string, string?> RawValues,
    IReadOnlyDictionary<string, string?> NormalisedValues,
    IReadOnlyList<ImportRowErrorResponse> Errors);

/// <summary>One field a target accepts.</summary>
/// <param name="Name">The field's canonical name, as a binding names it.</param>
/// <param name="Type">What a cell is parsed into — <c>Text</c>, <c>Integer</c>, <c>Decimal</c>, <c>Money</c>, <c>Quantity</c>, <c>Date</c> or <c>Boolean</c>.</param>
/// <param name="IsRequired">True when a batch cannot be mapped without binding it.</param>
/// <param name="Description">What the field means, in a sentence a shopkeeper would read.</param>
/// <param name="Example">A well-formed value.</param>
/// <param name="Aliases">Header texts that mean this field, matched ignoring case, spacing and punctuation.</param>
public sealed record ImportFieldResponse(
    string Name,
    string Type,
    bool IsRequired,
    string Description,
    string Example,
    IReadOnlyList<string> Aliases);

/// <summary>What one target accepts — everything a mapping screen needs to build itself.</summary>
/// <param name="Kind">The target.</param>
/// <param name="Name">Its name, for a person.</param>
/// <param name="Description">What importing into it does.</param>
/// <param name="RequiresStore">True when a batch aimed at this target must name a store.</param>
/// <param name="NaturalKeyFields">The fields that together decide whether a row creates or updates.</param>
/// <param name="Fields">The field catalogue.</param>
public sealed record ImportTargetResponse(
    string Kind,
    string Name,
    string Description,
    bool RequiresStore,
    IReadOnlyList<string> NaturalKeyFields,
    IReadOnlyList<ImportFieldResponse> Fields);

/// <summary>A saved mapping.</summary>
/// <param name="Id">The template.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="TargetKind">The target it maps to.</param>
/// <param name="SourceSignature">The hash of the header row it matches — sorted, so a reordered file still matches.</param>
/// <param name="IsActive">True while it may be matched.</param>
/// <param name="TimesUsed">How many uploads it has mapped.</param>
/// <param name="Bindings">The bindings it applies.</param>
public sealed record ImportMappingTemplateResponse(
    Guid Id,
    string Code,
    string Name,
    string TargetKind,
    string SourceSignature,
    bool IsActive,
    int TimesUsed,
    IReadOnlyList<ImportBindingContract> Bindings);
