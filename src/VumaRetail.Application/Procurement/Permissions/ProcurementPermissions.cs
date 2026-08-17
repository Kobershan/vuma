using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Procurement.Permissions;

/// <summary>
/// What the <c>procurement</c> module — requisitions, RFQs, purchase orders, goods receipts, the
/// three-way match and supplier scorecards — lets somebody do.
/// </summary>
/// <remarks>
/// <para>
/// The split follows the segregation of duties a buying function is built around, because in
/// procurement that separation <em>is</em> the control. <b>Raising a requisition is separated from
/// approving one</b>, so nobody authorises their own spending. <b>Awarding an RFQ is its own
/// permission</b>, because choosing which supplier wins is the single most valuable decision in the
/// chain and the one a kickback buys. <b>Receiving goods is separated from ordering them</b>, so the
/// person who wrote the order is not also the person who confirms it arrived. And <b>releasing a
/// matched invoice is separated from everything else</b>: that is the act that puts money on the way
/// out the door.
/// </para>
/// <para>
/// A shop small enough that one person does all four still gets the audit trail — the permissions are
/// granted to one role rather than collapsed into one permission, so the separation is available the
/// day the shop is big enough to want it, without a migration.
/// </para>
/// </remarks>
public sealed class ProcurementPermissions : IModulePermissions
{
    /// <summary>
    /// See requisitions, RFQs, orders, receipts, matches and scorecards.
    /// </summary>
    /// <remarks>
    /// <c>procurement.document.view</c> rather than <c>procurement.view</c>: <c>PermissionKey</c> is
    /// exactly three segments, and a two-segment key is refused when the catalogue is built — which
    /// takes the whole container down at startup, not the one endpoint that uses it.
    /// </remarks>
    public const string View = "procurement.document.view";

    /// <summary>Raise a requisition and put lines on it.</summary>
    public const string RequisitionRaise = "procurement.requisition.raise";

    /// <summary>Approve or reject a submitted requisition.</summary>
    public const string RequisitionApprove = "procurement.requisition.approve";

    /// <summary>Create RFQs and record the quotes that come back.</summary>
    public const string RfqManage = "procurement.rfq.manage";

    /// <summary>Choose which supplier's quote wins.</summary>
    public const string RfqAward = "procurement.rfq.award";

    /// <summary>Create and amend purchase orders.</summary>
    public const string OrderManage = "procurement.order.manage";

    /// <summary>Approve a purchase order and send it to the supplier. The commitment.</summary>
    public const string OrderIssue = "procurement.order.issue";

    /// <summary>Check a delivery in against an order.</summary>
    public const string ReceiptRecord = "procurement.receipt.record";

    /// <summary>Run a three-way match against a supplier's invoice.</summary>
    public const string InvoiceMatch = "procurement.invoice.match";

    /// <summary>Release a matched invoice for payment. The act that raises the financial event.</summary>
    public const string InvoiceRelease = "procurement.invoice.release";

    /// <summary>Take a supplier scorecard snapshot for a closed period.</summary>
    public const string ScorecardManage = "procurement.scorecard.manage";

    /// <inheritdoc />
    public string Module => "procurement";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(View), "See procurement documents and supplier scorecards."),
        new(PermissionKey.Parse(RequisitionRaise), "Raise a purchase requisition."),
        new(
            PermissionKey.Parse(RequisitionApprove),
            "Approve or reject a requisition.",
            IsHighRisk: true),
        new(PermissionKey.Parse(RfqManage), "Create RFQs and record supplier quotes."),
        new(
            PermissionKey.Parse(RfqAward),
            "Award an RFQ to a supplier's quote.",
            IsHighRisk: true),
        new(PermissionKey.Parse(OrderManage), "Create and amend purchase orders.", IsHighRisk: true),
        new(
            PermissionKey.Parse(OrderIssue),
            "Approve a purchase order and send it to the supplier.",
            IsHighRisk: true),
        new(PermissionKey.Parse(ReceiptRecord), "Check a delivery in against an order."),
        new(PermissionKey.Parse(InvoiceMatch), "Match a supplier invoice against an order and its receipts."),
        new(
            PermissionKey.Parse(InvoiceRelease),
            "Release a matched supplier invoice for payment.",
            IsHighRisk: true),
        new(PermissionKey.Parse(ScorecardManage), "Take a supplier scorecard snapshot."),
    ];
}

/// <summary>
/// The <c>procurement</c> module's manifest (R7).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not core.</b> A shop can trade without it, and plenty do: a single-site retailer who buys from
/// two suppliers by phoning them and files the invoices in a folder needs a stock ledger and books, not
/// a requisition workflow. What they cannot do without this module is prove that what they paid for is
/// what arrived — which is exactly the kind of thing a growing customer chooses to buy.
/// </para>
/// <para>
/// One flag across all six documents rather than a split, and the three-way match is why. Splitting
/// receiving from matching would let a tenant buy the ability to take stock in without the ability to
/// check the bill against it, which is the one combination that makes the module worth less than
/// nothing — it would create the paperwork and discard the control.
/// </para>
/// </remarks>
public sealed class ProcurementModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "procurement";

    /// <inheritdoc />
    public string LicenceFlag => "procurement";

    /// <inheritdoc />
    public string Description
        => "Procurement — requisitions, RFQs, purchase orders, goods receipts, three-way match and "
        + "supplier scorecards.";

    /// <inheritdoc />
    public bool IsCore => false;
}
