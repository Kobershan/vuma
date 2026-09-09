using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.FieldSales.Permissions;

/// <summary>
/// What the field-sales module lets somebody do (Stage 14b).
/// </summary>
/// <remarks>
/// Capturing and submitting are separate grants because a junior rep may capture while only a
/// senior (or the rep after review) submits; approving is the high-risk act that commits stock
/// and money through the saga. Performance splits own/team so a rep sees their own month without
/// seeing the team's. Cost is never exposed anywhere in this module (FIELD_SALES.md rule 4), so
/// there is deliberately no cost permission to gate — when a future surface carries cost, its
/// permission lands with it.
/// </remarks>
public sealed class FieldSalesPermissions : IModulePermissions
{
    /// <summary>Capture a pro forma order or credit note.</summary>
    public const string ProFormaCapture = "fieldsales.proforma.capture";

    /// <summary>Submit a pro forma for approval.</summary>
    public const string ProFormaSubmit = "fieldsales.proforma.submit";

    /// <summary>Read pro formas and availability.</summary>
    public const string ProFormaView = "fieldsales.proforma.view";

    /// <summary>Approve or reject a submitted pro forma. Commits stock and money.</summary>
    public const string ProFormaApprove = "fieldsales.proforma.approve";

    /// <summary>Read one's own performance.</summary>
    public const string PerformanceOwn = "fieldsales.performance.own";

    /// <summary>Read the team's performance.</summary>
    public const string PerformanceTeam = "fieldsales.performance.team";

    /// <inheritdoc />
    public string Module => "fieldsales";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(ProFormaCapture), "Capture a pro forma order or credit note."),
        new(PermissionKey.Parse(ProFormaSubmit), "Submit a pro forma for approval."),
        new(PermissionKey.Parse(ProFormaView), "Read pro formas and rep availability."),
        new(PermissionKey.Parse(ProFormaApprove), "Approve or reject a submitted pro forma.", IsHighRisk: true),
        new(PermissionKey.Parse(PerformanceOwn), "Read one's own performance."),
        new(PermissionKey.Parse(PerformanceTeam), "Read the team's performance."),
    ];
}

/// <summary>
/// The field-sales module's manifest (R7).
/// </summary>
public sealed class FieldSalesModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "fieldsales";

    /// <inheritdoc />
    public string LicenceFlag => "fieldsales";

    /// <inheritdoc />
    public string Description => "Field sales — rep pro formas, approval-to-order, performance.";

    /// <inheritdoc />
    public bool IsCore => false;
}
