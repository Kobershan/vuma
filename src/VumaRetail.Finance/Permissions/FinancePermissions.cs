using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Finance.Permissions;

/// <summary>What the <c>finance</c> module lets somebody do.</summary>
public sealed class FinancePermissions : IModulePermissions
{
    /// <summary>See the chart of accounts, journals, trial balance and account balances.</summary>
    public const string LedgerView = "finance.ledger.view";

    /// <summary>Create and edit accounts and posting rules.</summary>
    public const string LedgerConfigure = "finance.ledger.configure";

    /// <summary>Post a manual journal directly.</summary>
    public const string LedgerPost = "finance.ledger.post";

    /// <summary>Reverse a posted journal.</summary>
    public const string LedgerReverse = "finance.ledger.reverse";

    /// <summary>Open and close accounting periods.</summary>
    public const string PeriodManage = "finance.period.manage";

    /// <summary>See AR invoices, receipts and the ageing report.</summary>
    public const string ArView = "finance.ar.view";

    /// <summary>Create and post AR invoices.</summary>
    public const string ArInvoice = "finance.ar.invoice";

    /// <summary>Record a customer receipt.</summary>
    public const string ArReceipt = "finance.ar.receipt";

    /// <summary>See AP invoices, payments and the ageing report.</summary>
    public const string ApView = "finance.ap.view";

    /// <summary>Capture and post AP invoices.</summary>
    public const string ApInvoice = "finance.ap.invoice";

    /// <summary>Record a supplier payment.</summary>
    public const string ApPayment = "finance.ap.payment";

    /// <summary>See bank accounts and statement lines.</summary>
    public const string BankingView = "finance.banking.view";

    /// <summary>Manage bank accounts, import statement lines, match and unmatch them.</summary>
    public const string BankingReconcile = "finance.banking.reconcile";

    /// <summary>See tax rules.</summary>
    public const string TaxView = "finance.tax.view";

    /// <summary>Create and retire tax rules.</summary>
    public const string TaxConfigure = "finance.tax.configure";

    /// <inheritdoc />
    public string Module => "finance";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(LedgerView), "See the chart of accounts, journals and trial balance."),
        new(PermissionKey.Parse(LedgerConfigure), "Create accounts and configure posting rules.", IsHighRisk: true),
        new(PermissionKey.Parse(LedgerPost), "Post a manual journal.", IsHighRisk: true),
        new(PermissionKey.Parse(LedgerReverse), "Reverse a posted journal.", IsHighRisk: true),
        new(PermissionKey.Parse(PeriodManage), "Open and close accounting periods.", IsHighRisk: true),
        new(PermissionKey.Parse(ArView), "See customer invoices, receipts and ageing."),
        new(PermissionKey.Parse(ArInvoice), "Create and post customer invoices."),
        new(PermissionKey.Parse(ArReceipt), "Record a customer receipt.", IsHighRisk: true),
        new(PermissionKey.Parse(ApView), "See supplier invoices, payments and ageing."),
        new(PermissionKey.Parse(ApInvoice), "Capture and post supplier invoices."),
        new(PermissionKey.Parse(ApPayment), "Record a supplier payment.", IsHighRisk: true),
        new(PermissionKey.Parse(BankingView), "See bank accounts and statement lines."),
        new(PermissionKey.Parse(BankingReconcile), "Import and match bank statement lines.", IsHighRisk: true),
        new(PermissionKey.Parse(TaxView), "See tax rules."),
        new(PermissionKey.Parse(TaxConfigure), "Create and retire tax rules.", IsHighRisk: true),
    ];
}

/// <summary>
/// The <c>finance</c> module's manifest (R7).
/// </summary>
/// <remarks>
/// Core (ADR-064). Every other module speaks <c>IFinancialEvent</c> and gets a journal back
/// (CLAUDE.md §7 rule 12) — a disabled Finance module would not remove a feature the way a disabled
/// Loyalty or Ecommerce module does, it would make every posting write in the system fail, including
/// a POS sale under R1's "POS never stops". That is the same shape of dependency
/// <c>CatalogModuleManifest</c> and <c>PartnersModuleManifest</c> were judged core for at Stage 06 —
/// AR needs a customer to exist and Finance needs an item's tax class resolved by the time Stage 08/09
/// land — except Finance sits one layer lower again: it is the only place a GL account may be named,
/// so nothing downstream can route around it if it is off.
/// </remarks>
public sealed class FinanceModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "finance";

    /// <inheritdoc />
    public string LicenceFlag => "finance";

    /// <inheritdoc />
    public string Description => "General ledger, accounts receivable, accounts payable, banking and tax.";

    /// <inheritdoc />
    public bool IsCore => true;
}
