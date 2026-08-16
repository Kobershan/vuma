using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Imports.Permissions;

/// <summary>
/// What the <c>imports</c> module lets somebody do.
/// </summary>
/// <remarks>
/// <para>
/// The split follows the pipeline's own safety model rather than the endpoints. <b>Uploading,
/// mapping and validating are one permission</b>, because none of them touches a business table —
/// that is business rule 1, and it is what makes a preview safe to hand to somebody who is not
/// trusted with the commit. <b>Committing is its own high-risk permission</b>, because it is the
/// moment four thousand rows become real.
/// </para>
/// <para>
/// <b>Rollback is separate from commit, and also high risk.</b> They are not the same authority:
/// committing writes what a person has just previewed, while rolling back un-writes work that other
/// people may have been relying on for a week. A shop may well want the buyer to be able to load a
/// price list and only the owner to be able to take one back.
/// </para>
/// </remarks>
public sealed class ImportsPermissions : IModulePermissions
{
    /// <summary>See import batches, their rows and the preview.</summary>
    public const string BatchView = "imports.batch.view";

    /// <summary>Upload a file, set its mapping, and validate it. Writes nothing outside <c>imports</c>.</summary>
    public const string BatchCreate = "imports.batch.create";

    /// <summary>Apply a validated batch to its target module.</summary>
    public const string BatchCommit = "imports.batch.commit";

    /// <summary>Take back everything a committed batch did.</summary>
    public const string BatchRollback = "imports.batch.rollback";

    /// <summary>Save and retire the mappings that make next month's file map itself.</summary>
    public const string TemplateManage = "imports.template.manage";

    /// <inheritdoc />
    public string Module => "imports";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(BatchView), "See imports, their rows and the preview."),
        new(
            PermissionKey.Parse(BatchCreate),
            "Upload, map and validate a file. Nothing outside the imports schema is written."),
        new(
            PermissionKey.Parse(BatchCommit),
            "Apply a validated import to suppliers, customers, items, stock or prices.",
            IsHighRisk: true),
        new(
            PermissionKey.Parse(BatchRollback),
            "Take back a committed import, undoing work others may be relying on.",
            IsHighRisk: true),
        new(PermissionKey.Parse(TemplateManage), "Save and retire mapping templates."),
    ];
}

/// <summary>
/// The <c>imports</c> module's manifest (R7).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not core.</b> A shop that keys its own data in never needs this, and plenty do — a hardware
/// store with four hundred lines types them once and is done. What the module buys is the difference
/// between starting to trade this week and starting next month, which is exactly the kind of thing a
/// customer chooses to pay for at the point they are migrating off something else.
/// </para>
/// <para>
/// One flag rather than one per target. Splitting it would let a tenant buy the ability to import a
/// price list and not a supplier list, which is not a distinction anybody has ever asked for, and
/// would leave the pipeline — readers, mapping, preview, rollback, all of the actual work — half
/// licensed.
/// </para>
/// </remarks>
public sealed class ImportsModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "imports";

    /// <inheritdoc />
    public string LicenceFlag => "imports";

    /// <inheritdoc />
    public string Description
        => "Data import — Excel, CSV and PDF ingestion with mapping, preview, validation and rollback.";

    /// <inheritdoc />
    public bool IsCore => false;
}
