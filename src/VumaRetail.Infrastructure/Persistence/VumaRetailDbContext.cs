using System.Reflection;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Platform;

namespace VumaRetail.Infrastructure.Persistence;

/// <summary>
/// The store server's and cloud tier's database context. One context, schema per module (ADR-010).
/// </summary>
/// <remarks>
/// <para>
/// A context per module was considered and rejected: modules share transactions constantly — a sale
/// writes to sales, inventory and the outbox atomically — and splitting the context would turn the
/// transactional outbox (ADR-006) into a distributed transaction problem inside a single process.
/// Boundaries are enforced by schemas, an architecture test and the no-cross-schema-foreign-key rule
/// instead, which cost nothing at runtime.
/// </para>
/// <para>
/// Two global query filters are applied to every entity from
/// <see cref="Configurations.EntityConfiguration{TEntity}"/>: soft delete (§7 rule 8) and tenant
/// isolation. They are applied by convention rather than per entity because the failure mode of
/// "somebody forgot one" is a tenant reading another tenant's trading data.
/// </para>
/// </remarks>
public class VumaRetailDbContext : DbContext, IUnitOfWork
{
    private readonly ITenantContext _tenantContext;
    private readonly ICompanyContext? _companyContext;

    /// <summary>Creates the context.</summary>
    /// <param name="options">EF options, including the Npgsql provider and the interceptors.</param>
    /// <param name="tenantContext">Supplies the tenant the global query filter scopes to.</param>
    /// <param name="companyContext">Supplies the active company for row stamping and filtering.</param>
    public VumaRetailDbContext(DbContextOptions options, ITenantContext tenantContext, ICompanyContext? companyContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
        _companyContext = companyContext;
    }

    /// <summary>Tenants — the isolation root every other row hangs off.</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>Trading locations.</summary>
    public DbSet<Store> Stores => Set<Store>();

    /// <summary>The immutable audit trail (R6). Written by the interceptor, never by business code.</summary>
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <summary>People who sign in — back office, till, or both (Stage 02).</summary>
    public DbSet<Domain.Identity.User> Users => Set<Domain.Identity.User>();

    /// <summary>Named bags of permissions (ADR-013).</summary>
    public DbSet<Domain.Identity.Role> Roles => Set<Domain.Identity.Role>();

    /// <summary>One permission granted to one role.</summary>
    public DbSet<Domain.Identity.RolePermission> RolePermissions => Set<Domain.Identity.RolePermission>();

    /// <summary>A user holding a role, tenant-wide or in one store.</summary>
    public DbSet<Domain.Identity.UserRoleAssignment> UserRoleAssignments => Set<Domain.Identity.UserRoleAssignment>();

    /// <summary>Enrolled tills and back-office machines.</summary>
    public DbSet<Domain.Identity.Terminal> Terminals => Set<Domain.Identity.Terminal>();

    /// <summary>Issued refresh tokens, stored as digests and rotated on use.</summary>
    public DbSet<Domain.Identity.RefreshToken> RefreshTokens => Set<Domain.Identity.RefreshToken>();

    /// <summary>The transactional outbox — changes waiting to reach the next tier (Stage 04, ADR-006).</summary>
    public DbSet<Domain.Sync.OutboxMessage> OutboxMessages => Set<Domain.Sync.OutboxMessage>();

    /// <summary>The idempotent inbox — what this node has already processed (ADR-006).</summary>
    public DbSet<Domain.Sync.InboxMessage> InboxMessages => Set<Domain.Sync.InboxMessage>();

    /// <summary>How far each peer has got, per direction.</summary>
    public DbSet<Domain.Sync.SyncCursor> SyncCursors => Set<Domain.Sync.SyncCursor>();

    /// <summary>Divergences waiting for a person, with both versions kept (ADR-007).</summary>
    public DbSet<Domain.Sync.ConflictEntry> ConflictEntries => Set<Domain.Sync.ConflictEntry>();

    /// <summary>The snapshot ledger — requirement R4's evidence.</summary>
    public DbSet<Domain.Backup.BackupSnapshot> BackupSnapshots => Set<Domain.Backup.BackupSnapshot>();

    /// <summary>This installation's binding to a licence key and a machine (Stage 04b).</summary>
    public DbSet<Domain.Licensing.Activation> Activations => Set<Domain.Licensing.Activation>();

    /// <summary>The signed monthly licences, newest by issuance counter.</summary>
    public DbSet<Domain.Licensing.Licence> Licences => Set<Domain.Licensing.Licence>();

    /// <summary>The 72-hour leases the software actually runs on.</summary>
    public DbSet<Domain.Licensing.Lease> Leases => Set<Domain.Licensing.Lease>();

    /// <summary>Vendor emergency access codes redeemed here, single-use by unique index.</summary>
    public DbSet<Domain.Licensing.EmergencyUnlock> EmergencyUnlocks => Set<Domain.Licensing.EmergencyUnlock>();

    /// <summary>What the client-side hardening noticed. Reported to the vendor; restricts nobody.</summary>
    public DbSet<Domain.Licensing.TamperFlag> TamperFlags => Set<Domain.Licensing.TamperFlag>();

    /// <summary>The highest wall-clock instant this installation has ever seen.</summary>
    public DbSet<Domain.Licensing.ClockWatermark> ClockWatermarks => Set<Domain.Licensing.ClockWatermark>();

    /// <summary>Daily usage rollups — counts and health only (R10).</summary>
    public DbSet<Domain.Licensing.MeteringRecord> MeteringRecords => Set<Domain.Licensing.MeteringRecord>();

    /// <summary>Tenant-granted, time-boxed vendor support access.</summary>
    public DbSet<Domain.Licensing.SupportGrant> SupportGrants => Set<Domain.Licensing.SupportGrant>();

    /// <summary>Configured gates on threshold-sensitive actions (Stage 05, ADR-019).</summary>
    public DbSet<Domain.Workflow.ApprovalPolicy> ApprovalPolicies => Set<Domain.Workflow.ApprovalPolicy>();

    /// <summary>Pending and decided approval requests — the unified inbox's own rows.</summary>
    public DbSet<Domain.Workflow.ApprovalRequest> ApprovalRequests => Set<Domain.Workflow.ApprovalRequest>();

    /// <summary>The append-only approval decision history.</summary>
    public DbSet<Domain.Workflow.ApprovalDecisionEntry> ApprovalDecisionEntries => Set<Domain.Workflow.ApprovalDecisionEntry>();

    /// <summary>One message to one recipient on one channel.</summary>
    public DbSet<Domain.Workflow.Notification> Notifications => Set<Domain.Workflow.Notification>();

    /// <summary>Document metadata — never the bytes, which live behind <c>IDocumentBlobStore</c>.</summary>
    public DbSet<Domain.Workflow.Document> Documents => Set<Domain.Workflow.Document>();

    /// <summary>The append-only document version history.</summary>
    public DbSet<Domain.Workflow.DocumentVersion> DocumentVersions => Set<Domain.Workflow.DocumentVersion>();

    /// <summary>Tenant-scoped conversational commerce state and delivery tokens (Stage 22b).</summary>
    public DbSet<Domain.Conversations.Conversation> Conversations => Set<Domain.Conversations.Conversation>();

    /// <summary>Append-only conversational transcript entries (Stage 22b).</summary>
    public DbSet<Domain.Conversations.ConversationTurn> ConversationTurns => Set<Domain.Conversations.ConversationTurn>();

    /// <summary>Single-use, expiring references to documents delivered by conversations.</summary>
    public DbSet<Domain.Conversations.DocumentDeliveryToken> DocumentDeliveryTokens => Set<Domain.Conversations.DocumentDeliveryToken>();
    /// <summary>Units an item can be counted, weighed or measured in (Stage 06).</summary>
    public DbSet<Domain.Catalog.UnitOfMeasure> UnitsOfMeasure => Set<Domain.Catalog.UnitOfMeasure>();

    /// <summary>Products and services a tenant sells or stocks.</summary>
    public DbSet<Domain.Catalog.Item> Items => Set<Domain.Catalog.Item>();

    /// <summary>Sellable variations of an item.</summary>
    public DbSet<Domain.Catalog.ItemVariant> ItemVariants => Set<Domain.Catalog.ItemVariant>();

    /// <summary>Scannable codes identifying an item or a variant at the till.</summary>
    public DbSet<Domain.Catalog.Barcode> Barcodes => Set<Domain.Catalog.Barcode>();

    /// <summary>Suppliers, customers, and partners who are both (Stage 06).</summary>
    public DbSet<Domain.Partners.Partner> Partners => Set<Domain.Partners.Partner>();

    /// <summary>Tenant employees (Stage 25).</summary>
    public DbSet<Domain.HrManagement.Employee> Employees => Set<Domain.HrManagement.Employee>();
    public DbSet<Domain.HrManagement.EmploymentContract> EmploymentContracts => Set<Domain.HrManagement.EmploymentContract>();
    public DbSet<Domain.HrManagement.LeaveRequest> LeaveRequests => Set<Domain.HrManagement.LeaveRequest>();
    public DbSet<Domain.HrWorkforce.Shift> Shifts => Set<Domain.HrWorkforce.Shift>();
    public DbSet<Domain.HrWorkforce.AttendanceRecord> AttendanceRecords => Set<Domain.HrWorkforce.AttendanceRecord>();

    /// <summary>Physical places stock is held — warehouses, sales floors (Stage 08).</summary>
    public DbSet<Domain.Inventory.StockLocation> StockLocations => Set<Domain.Inventory.StockLocation>();

    /// <summary>The append-only stock ledger. Nothing here is ever updated or deleted (ADR-005).</summary>
    public DbSet<Domain.Inventory.StockLedgerEntry> StockLedgerEntries => Set<Domain.Inventory.StockLedgerEntry>();

    /// <summary>On-hand quantity and weighted-average cost, the projection the ledger sums to.</summary>
    public DbSet<Domain.Inventory.StockBalance> StockBalances => Set<Domain.Inventory.StockBalance>();

    /// <summary>The document correlating the two ledger entries a transfer posts.</summary>
    public DbSet<Domain.Inventory.StockTransfer> StockTransfers => Set<Domain.Inventory.StockTransfer>();

    /// <summary>Physical count sessions.</summary>
    public DbSet<Domain.Inventory.StocktakeSession> StocktakeSessions => Set<Domain.Inventory.StocktakeSession>();

    /// <summary>One counted stock-keeping unit within a session.</summary>
    public DbSet<Domain.Inventory.StocktakeLine> StocktakeLines => Set<Domain.Inventory.StocktakeLine>();

    /// <summary>The append-only reservation hold ledger. Releases are new rows, never edits (Stage 08c, ADR-103).</summary>
    public DbSet<Domain.Inventory.StockReservation> StockReservations => Set<Domain.Inventory.StockReservation>();

    /// <summary>Reserved / staging / incoming positions, the projection the reservation ledger sums to (Stage 08c).</summary>
    public DbSet<Domain.Inventory.AvailableBalance> AvailableBalances => Set<Domain.Inventory.AvailableBalance>();

    /// <summary>A cashier's shift at one terminal, and the cash-up that closes it (Stage 09).</summary>
    public DbSet<Domain.Pos.TillSession> TillSessions => Set<Domain.Pos.TillSession>();

    /// <summary>Transactions at the till.</summary>
    public DbSet<Domain.Pos.Sale> Sales => Set<Domain.Pos.Sale>();

    /// <summary>What was rung up on a sale.</summary>
    public DbSet<Domain.Pos.SaleLine> SaleLines => Set<Domain.Pos.SaleLine>();

    /// <summary>How a sale was paid for. Immutable once captured.</summary>
    public DbSet<Domain.Pos.SaleTender> SaleTenders => Set<Domain.Pos.SaleTender>();

    /// <summary>The append-only record of every receipt printed and reprinted.</summary>
    public DbSet<Domain.Pos.ReceiptPrint> ReceiptPrints => Set<Domain.Pos.ReceiptPrint>();

    /// <summary>Named sets of prices — retail, wholesale, staff (Stage 10).</summary>
    public DbSet<Domain.Sales.PriceList> PriceLists => Set<Domain.Sales.PriceList>();

    /// <summary>One price on a list, from one quantity upwards.</summary>
    public DbSet<Domain.Sales.PriceListLine> PriceListLines => Set<Domain.Sales.PriceListLine>();

    /// <summary>Specials. Configuration, never a deployment.</summary>
    public DbSet<Domain.Sales.Promotion> Promotions => Set<Domain.Sales.Promotion>();

    /// <summary>What a promotion applies to.</summary>
    public DbSet<Domain.Sales.PromotionLine> PromotionLines => Set<Domain.Sales.PromotionLine>();

    /// <summary>The documents that take goods and money back.</summary>
    public DbSet<Domain.Sales.SalesReturn> SalesReturns => Set<Domain.Sales.SalesReturn>();

    /// <summary>One line coming back, refunded at what was actually charged.</summary>
    public DbSet<Domain.Sales.SalesReturnLine> SalesReturnLines => Set<Domain.Sales.SalesReturnLine>();

    /// <summary>The append-only record of every sale made at something other than the resolved price.</summary>
    public DbSet<Domain.Sales.PriceOverrideLog> PriceOverrideLogs => Set<Domain.Sales.PriceOverrideLog>();

    /// <summary>Non-binding price promises with a lifecycle (Stage 10c).</summary>
    public DbSet<Domain.Sales.Quotes.Quote> Quotes => Set<Domain.Sales.Quotes.Quote>();

    /// <summary>Lines on a quote, carrying price and pack size snapshots.</summary>
    public DbSet<Domain.Sales.Quotes.QuoteLine> QuoteLines => Set<Domain.Sales.Quotes.QuoteLine>();

    /// <summary>Legally binding documents, immutable once posted (Stage 10c).</summary>
    public DbSet<Domain.Sales.Invoices.Invoice> Invoices => Set<Domain.Sales.Invoices.Invoice>();

    /// <summary>Lines on an invoice, carrying pack size snapshots.</summary>
    public DbSet<Domain.Sales.Invoices.InvoiceLine> InvoiceLines => Set<Domain.Sales.Invoices.InvoiceLine>();

    /// <summary>Company-scoped sales read models (Stage 10c).</summary>
    public DbSet<Domain.Sales.Analytics.SalesAnalytics> SalesAnalytics => Set<Domain.Sales.Analytics.SalesAnalytics>();

    /// <summary>Customer credit accounts with limits, terms and standing (Stage 10b).</summary>
    public DbSet<Domain.CustomerAccounts.CustomerAccount> CustomerAccounts => Set<Domain.CustomerAccounts.CustomerAccount>();

    /// <summary>Named buyers authorised to charge to a business account (Stage 10b).</summary>
    public DbSet<Domain.CustomerAccounts.AccountHolder> AccountHolders => Set<Domain.CustomerAccounts.AccountHolder>();

    /// <summary>The tenant's customer-money policy row, one per tenant (Stage 10b).</summary>
    public DbSet<Domain.CustomerAccounts.CustomerFinanceTerms> CustomerFinanceTerms => Set<Domain.CustomerAccounts.CustomerFinanceTerms>();

    /// <summary>Lay-by agreements: frozen price, payment plan, held stock (Stage 10b).</summary>
    public DbSet<Domain.CustomerAccounts.LayByAgreement> LayByAgreements => Set<Domain.CustomerAccounts.LayByAgreement>();

    /// <summary>Lines on a lay-by agreement, carrying price and pack size snapshots (Stage 10b).</summary>
    public DbSet<Domain.CustomerAccounts.LayByAgreementLine> LayByAgreementLines => Set<Domain.CustomerAccounts.LayByAgreementLine>();

    /// <summary>Append-only lay-by payments (Stage 10b).</summary>
    public DbSet<Domain.CustomerAccounts.LayByInstalment> LayByInstalments => Set<Domain.CustomerAccounts.LayByInstalment>();

    /// <summary>Stokvel saving circles with a cycle and a constitution (Stage 10b).</summary>
    public DbSet<Domain.CustomerAccounts.StokvelGroup> StokvelGroups => Set<Domain.CustomerAccounts.StokvelGroup>();

    /// <summary>Members of a stokvel group: roles, join/leave instants, obligations (Stage 10b).</summary>
    public DbSet<Domain.CustomerAccounts.StokvelMember> StokvelMembers => Set<Domain.CustomerAccounts.StokvelMember>();

    /// <summary>Append-only stokvel member receipts (Stage 10b).</summary>
    public DbSet<Domain.CustomerAccounts.StokvelContribution> StokvelContributions => Set<Domain.CustomerAccounts.StokvelContribution>();

    /// <summary>Append-only time-weighted benefit shares (Stage 10b).</summary>
    public DbSet<Domain.CustomerAccounts.StokvelBenefitAllocation> StokvelBenefitAllocations => Set<Domain.CustomerAccounts.StokvelBenefitAllocation>();

    /// <summary>Member draw-downs: goods, hampers, cash or store credit (Stage 10b).</summary>
    public DbSet<Domain.CustomerAccounts.StokvelPayout> StokvelPayouts => Set<Domain.CustomerAccounts.StokvelPayout>();

    /// <summary>Predefined baskets at a frozen group price (Stage 10b).</summary>
    public DbSet<Domain.CustomerAccounts.HamperBasket> HamperBaskets => Set<Domain.CustomerAccounts.HamperBasket>();

    /// <summary>Frozen hamper contents with substitution rules (Stage 10b).</summary>
    public DbSet<Domain.CustomerAccounts.HamperBasketLine> HamperBasketLines => Set<Domain.CustomerAccounts.HamperBasketLine>();

    /// <summary>Field sales reps with territories and visibility profiles (Stage 14b).</summary>
    public DbSet<Domain.FieldSales.Rep> Reps => Set<Domain.FieldSales.Rep>();

    /// <summary>Rep pro forma orders: proposals, posting nothing (Stage 14b).</summary>
    public DbSet<Domain.FieldSales.ProFormaOrder> ProFormaOrders => Set<Domain.FieldSales.ProFormaOrder>();

    /// <summary>Snapshotted pro forma lines (Stage 14b).</summary>
    public DbSet<Domain.FieldSales.ProFormaOrderLine> ProFormaOrderLines => Set<Domain.FieldSales.ProFormaOrderLine>();

    /// <summary>Rep pro forma credit notes (Stage 14b).</summary>
    public DbSet<Domain.FieldSales.ProFormaCreditNote> ProFormaCreditNotes => Set<Domain.FieldSales.ProFormaCreditNote>();

    /// <summary>Snapshotted credit proposal lines (Stage 14b).</summary>
    public DbSet<Domain.FieldSales.ProFormaCreditNoteLine> ProFormaCreditNoteLines => Set<Domain.FieldSales.ProFormaCreditNoteLine>();

    /// <summary>Versioned rep targets (Stage 14b).</summary>
    public DbSet<Domain.FieldSales.RepTarget> RepTargets => Set<Domain.FieldSales.RepTarget>();

    /// <summary>Immutable closed-month rep performance (Stage 14b).</summary>
    public DbSet<Domain.FieldSales.RepPerformanceSnapshot> RepPerformanceSnapshots => Set<Domain.FieldSales.RepPerformanceSnapshot>();

    /// <summary>One uploaded file and the whole life of what it became (Stage 11).</summary>
    public DbSet<Domain.Imports.ImportBatch> ImportBatches => Set<Domain.Imports.ImportBatch>();

    /// <summary>One source row, its verdict, what it produced, and the before-image a rollback restores.</summary>
    public DbSet<Domain.Imports.ImportRow> ImportRows => Set<Domain.Imports.ImportRow>();

    /// <summary>One target field bound to one source column, or to a constant.</summary>
    public DbSet<Domain.Imports.ImportColumnMapping> ImportColumnMappings
        => Set<Domain.Imports.ImportColumnMapping>();

    /// <summary>A saved mapping, so the same supplier's file maps itself next month.</summary>
    public DbSet<Domain.Imports.ImportMappingTemplate> ImportMappingTemplates
        => Set<Domain.Imports.ImportMappingTemplate>();

    /// <summary>Internal demand — somebody saying they need something (Stage 12).</summary>
    public DbSet<Domain.Procurement.PurchaseRequisition> PurchaseRequisitions
        => Set<Domain.Procurement.PurchaseRequisition>();

    /// <summary>One thing a requisition asks for.</summary>
    public DbSet<Domain.Procurement.PurchaseRequisitionLine> PurchaseRequisitionLines
        => Set<Domain.Procurement.PurchaseRequisitionLine>();

    /// <summary>The buyer asking suppliers what they would charge.</summary>
    public DbSet<Domain.Procurement.Rfq> Rfqs => Set<Domain.Procurement.Rfq>();

    /// <summary>One thing suppliers are being asked to price.</summary>
    public DbSet<Domain.Procurement.RfqLine> RfqLines => Set<Domain.Procurement.RfqLine>();

    /// <summary>One supplier's quote, frozen on submission.</summary>
    public DbSet<Domain.Procurement.RfqResponse> RfqResponses => Set<Domain.Procurement.RfqResponse>();

    /// <summary>What one supplier charges for one RFQ line.</summary>
    public DbSet<Domain.Procurement.RfqResponseLine> RfqResponseLines
        => Set<Domain.Procurement.RfqResponseLine>();

    /// <summary>The commitment — the document a supplier holds the shop to.</summary>
    public DbSet<Domain.Procurement.PurchaseOrder> PurchaseOrders => Set<Domain.Procurement.PurchaseOrder>();

    /// <summary>One thing the shop committed to buy, at a stated cost.</summary>
    public DbSet<Domain.Procurement.PurchaseOrderLine> PurchaseOrderLines
        => Set<Domain.Procurement.PurchaseOrderLine>();

    /// <summary>The claim that the goods physically arrived.</summary>
    public DbSet<Domain.Procurement.GoodsReceipt> GoodsReceipts => Set<Domain.Procurement.GoodsReceipt>();

    /// <summary>What arrived against one order line, and what was sent back.</summary>
    public DbSet<Domain.Procurement.GoodsReceiptLine> GoodsReceiptLines
        => Set<Domain.Procurement.GoodsReceiptLine>();

    /// <summary>The three-way match: ordered against received against invoiced (ADR-082).</summary>
    public DbSet<Domain.Procurement.SupplierInvoiceMatch> SupplierInvoiceMatches
        => Set<Domain.Procurement.SupplierInvoiceMatch>();

    /// <summary>One line of the comparison, with its two variances.</summary>
    public DbSet<Domain.Procurement.SupplierInvoiceMatchLine> SupplierInvoiceMatchLines
        => Set<Domain.Procurement.SupplierInvoiceMatchLine>();

    /// <summary>One supplier's performance over one closed period, frozen (ADR-084).</summary>
    public DbSet<Domain.Procurement.SupplierScorecard> SupplierScorecards
        => Set<Domain.Procurement.SupplierScorecard>();

    /// <summary>Mutual supplier/retailer trading relationships (Stage 21b).</summary>
    public DbSet<Domain.Connect.TradingConnection> TradingConnections => Set<Domain.Connect.TradingConnection>();

    /// <summary>Supplier-issued invitation codes (Stage 21b).</summary>
    public DbSet<Domain.Connect.ConnectionCode> ConnectionCodes => Set<Domain.Connect.ConnectionCode>();

    /// <summary>Versioned supplier catalogue publications (Stage 21b).</summary>
    public DbSet<Domain.Connect.CataloguePublication> CataloguePublications => Set<Domain.Connect.CataloguePublication>();

    /// <summary>Lines in a catalogue publication.</summary>
    public DbSet<Domain.Connect.CataloguePublicationLine> CataloguePublicationLines => Set<Domain.Connect.CataloguePublicationLine>();

    /// <summary>Retailer-side price proposals.</summary>
    public DbSet<Domain.Connect.PriceProposal> PriceProposals => Set<Domain.Connect.PriceProposal>();

    /// <summary>Lines in a price proposal.</summary>
    public DbSet<Domain.Connect.PriceProposalLine> PriceProposalLines => Set<Domain.Connect.PriceProposalLine>();

    /// <summary>Connection-scoped purchase orders and supplier fulfilment state (Stage 21b).</summary>
    public DbSet<Domain.Connect.ConnectOrder> ConnectOrders => Set<Domain.Connect.ConnectOrder>();
    public DbSet<Domain.Connect.ConnectRemittanceAdvice> ConnectRemittances => Set<Domain.Connect.ConnectRemittanceAdvice>();
    public DbSet<Domain.Connect.ConnectDeliveryClaim> ConnectClaims => Set<Domain.Connect.ConnectDeliveryClaim>();

    /// <summary>Lines on connection-scoped purchase orders.</summary>
    public DbSet<Domain.Connect.ConnectOrderLine> ConnectOrderLines => Set<Domain.Connect.ConnectOrderLine>();

    /// <summary>A named subdivision of a Stage 08 location. Stage 13.</summary>
    public DbSet<Domain.Warehouse.Zone> Zones => Set<Domain.Warehouse.Zone>();

    /// <summary>A single storage position within a zone. Stage 13.</summary>
    public DbSet<Domain.Warehouse.Bin> Bins => Set<Domain.Warehouse.Bin>();

    /// <summary>On-hand quantity per bin per stock-keeping unit — the bin-level projection. Stage 13.</summary>
    public DbSet<Domain.Warehouse.BinStock> BinStocks => Set<Domain.Warehouse.BinStock>();

    /// <summary>The append-only bin-level movement ledger. Stage 13.</summary>
    public DbSet<Domain.Warehouse.BinStockMovement> BinStockMovements => Set<Domain.Warehouse.BinStockMovement>();

    /// <summary>One line of unbinned received stock waiting to be shelved. Stage 13.</summary>
    public DbSet<Domain.Warehouse.PutawayTask> PutawayTasks => Set<Domain.Warehouse.PutawayTask>();

    /// <summary>A batch of outbound demand to fulfil from one location. Stage 13.</summary>
    public DbSet<Domain.Warehouse.PickWave> PickWaves => Set<Domain.Warehouse.PickWave>();

    /// <summary>One demand line within a pick wave. Stage 13.</summary>
    public DbSet<Domain.Warehouse.PickTask> PickTasks => Set<Domain.Warehouse.PickTask>();

    /// <summary>A wave's packing record. Stage 13.</summary>
    public DbSet<Domain.Warehouse.PackTask> PackTasks => Set<Domain.Warehouse.PackTask>();

    /// <summary>The document recording a wave's shipment — stock leaving the location. Stage 13.</summary>
    public DbSet<Domain.Warehouse.ShipmentConfirmation> ShipmentConfirmations => Set<Domain.Warehouse.ShipmentConfirmation>();

    /// <summary>Stage 24 carrier directory.</summary>
    public DbSet<Domain.Logistics.Carrier> Carriers => Set<Domain.Logistics.Carrier>();
    /// <summary>Stage 24 delivery shipments.</summary>
    public DbSet<Domain.Logistics.Shipment> Shipments => Set<Domain.Logistics.Shipment>();
    /// <summary>Stage 24 delivery runs.</summary>
    public DbSet<Domain.Logistics.DeliveryRun> DeliveryRuns => Set<Domain.Logistics.DeliveryRun>();
    /// <summary>Stops assigned to delivery runs.</summary>
    public DbSet<Domain.Logistics.DeliveryStop> DeliveryStops => Set<Domain.Logistics.DeliveryStop>();
    /// <summary>Immutable proof that a shipment was delivered or refused.</summary>
    public DbSet<Domain.Logistics.ProofOfDelivery> ProofsOfDelivery => Set<Domain.Logistics.ProofOfDelivery>();

    /// <summary>A physical count of one or more bins. Stage 13.</summary>
    public DbSet<Domain.Warehouse.CycleCount> CycleCounts => Set<Domain.Warehouse.CycleCount>();

    /// <summary>One counted bin/stock-keeping-unit pair within a cycle count. Stage 13.</summary>
    public DbSet<Domain.Warehouse.CycleCountLine> CycleCountLines => Set<Domain.Warehouse.CycleCountLine>();

    /// <summary>Per-order split of a grouped wave line. Stage 13b.</summary>
    public DbSet<Domain.Warehouse.PickWaveLineBreakdown> PickWaveLineBreakdowns => Set<Domain.Warehouse.PickWaveLineBreakdown>();

    /// <summary>Scheduled count runs targeting slow movers. Stage 13b.</summary>
    public DbSet<Domain.Warehouse.CountSchedule> CountSchedules => Set<Domain.Warehouse.CountSchedule>();

    /// <summary>A promise to fulfil what a customer wants, by delivery or click &amp; collect. Stage 14.</summary>
    public DbSet<Domain.Orders.SalesOrder> SalesOrders => Set<Domain.Orders.SalesOrder>();

    /// <summary>One demand line on a sales order. Stage 14.</summary>
    public DbSet<Domain.Orders.SalesOrderLine> SalesOrderLines => Set<Domain.Orders.SalesOrderLine>();

    /// <summary>Goods fulfilled off a sales order coming back. Stage 14.</summary>
    public DbSet<Domain.Orders.SalesOrderReturn> SalesOrderReturns => Set<Domain.Orders.SalesOrderReturn>();

    /// <summary>One line coming back on a sales order return. Stage 14.</summary>
    public DbSet<Domain.Orders.SalesOrderReturnLine> SalesOrderReturnLines => Set<Domain.Orders.SalesOrderReturnLine>();

    /// <summary>The chart of accounts (Stage 07, ADR-016).</summary>
    public DbSet<Domain.Finance.Account> Accounts => Set<Domain.Finance.Account>();

    /// <summary>The tenant's accounting calendar.</summary>
    public DbSet<Domain.Finance.AccountingPeriod> AccountingPeriods => Set<Domain.Finance.AccountingPeriod>();

    /// <summary>The immutable general ledger.</summary>
    public DbSet<Domain.Finance.Journal> Journals => Set<Domain.Finance.Journal>();

    /// <summary>One debit or credit line of a posted journal.</summary>
    public DbSet<Domain.Finance.JournalLine> JournalLines => Set<Domain.Finance.JournalLine>();

    /// <summary>Tenant-configured mappings from a financial event type to GL postings.</summary>
    public DbSet<Domain.Finance.PostingRule> PostingRules => Set<Domain.Finance.PostingRule>();

    /// <summary>One posting a posting rule produces.</summary>
    public DbSet<Domain.Finance.PostingRuleLine> PostingRuleLines => Set<Domain.Finance.PostingRuleLine>();

    /// <summary>Customer invoices — the AR sub-ledger.</summary>
    public DbSet<Domain.Finance.ArInvoice> ArInvoices => Set<Domain.Finance.ArInvoice>();

    /// <summary>One line of a customer invoice.</summary>
    public DbSet<Domain.Finance.ArInvoiceLine> ArInvoiceLines => Set<Domain.Finance.ArInvoiceLine>();

    /// <summary>Payments received from customers.</summary>
    public DbSet<Domain.Finance.ArReceipt> ArReceipts => Set<Domain.Finance.ArReceipt>();

    /// <summary>How a customer receipt was allocated across invoices.</summary>
    public DbSet<Domain.Finance.ArReceiptAllocation> ArReceiptAllocations => Set<Domain.Finance.ArReceiptAllocation>();

    /// <summary>Supplier invoices — the AP sub-ledger.</summary>
    public DbSet<Domain.Finance.ApInvoice> ApInvoices => Set<Domain.Finance.ApInvoice>();

    /// <summary>One line of a supplier invoice.</summary>
    public DbSet<Domain.Finance.ApInvoiceLine> ApInvoiceLines => Set<Domain.Finance.ApInvoiceLine>();

    /// <summary>Payments made to suppliers.</summary>
    public DbSet<Domain.Finance.ApPayment> ApPayments => Set<Domain.Finance.ApPayment>();

    /// <summary>How a supplier payment was allocated across invoices.</summary>
    public DbSet<Domain.Finance.ApPaymentAllocation> ApPaymentAllocations => Set<Domain.Finance.ApPaymentAllocation>();

    /// <summary>Bank accounts, one per GL bank control account.</summary>
    public DbSet<Domain.Finance.BankAccount> BankAccounts => Set<Domain.Finance.BankAccount>();

    /// <summary>Imported bank statement lines and their reconciliation state.</summary>
    public DbSet<Domain.Finance.BankStatementLine> BankStatementLines => Set<Domain.Finance.BankStatementLine>();

    /// <summary>Tenant-configured tax rules — a rules engine, never a constant (CLAUDE.md §9).</summary>
    public DbSet<Domain.Finance.TaxRule> TaxRules => Set<Domain.Finance.TaxRule>();

    /// <summary>The evidence the daily control-account variance check leaves.</summary>
    public DbSet<Domain.Finance.ReconciliationVarianceFlag> ReconciliationVarianceFlags
        => Set<Domain.Finance.ReconciliationVarianceFlag>();

    /// <summary>Node-local document numbering state.</summary>
    public DbSet<Domain.Finance.DocumentNumberCounter> DocumentNumberCounters
        => Set<Domain.Finance.DocumentNumberCounter>();

    /// <summary>Demand history read model entries. Stage 15.</summary>
    public DbSet<Domain.Planning.DemandHistory> DemandHistories => Set<Domain.Planning.DemandHistory>();

    /// <summary>Versioned demand forecast snapshots. Stage 15.</summary>
    public DbSet<Domain.Planning.DemandForecast> DemandForecasts => Set<Domain.Planning.DemandForecast>();

    /// <summary>Replenishment parameters per SKU/location. Stage 15.</summary>
    public DbSet<Domain.Planning.ReplenishmentParameter> ReplenishmentParameters
        => Set<Domain.Planning.ReplenishmentParameter>();

    /// <summary>ABC/XYZ classification snapshots. Stage 15.</summary>
    public DbSet<Domain.Planning.AbcXyzClassification> AbcXyzClassifications
        => Set<Domain.Planning.AbcXyzClassification>();

    /// <summary>Safety-stock calculations. Stage 15.</summary>
    public DbSet<Domain.Planning.SafetyStockCalculation> SafetyStockCalculations
        => Set<Domain.Planning.SafetyStockCalculation>();

    /// <summary>Open-to-buy budgets. Stage 15.</summary>
    public DbSet<Domain.Planning.OpenToBuyBudget> OpenToBuyBudgets
        => Set<Domain.Planning.OpenToBuyBudget>();

    /// <summary>Replenishment suggestions. Stage 15.</summary>
    public DbSet<Domain.Planning.ReplenishmentSuggestion> ReplenishmentSuggestions
        => Set<Domain.Planning.ReplenishmentSuggestion>();

    /// <summary>Markdown plans. Stage 15.</summary>
    public DbSet<Domain.Planning.MarkdownPlan> MarkdownPlans => Set<Domain.Planning.MarkdownPlan>();

    /// <summary>Markdown plan lines. Stage 15.</summary>
    public DbSet<Domain.Planning.MarkdownPlanLine> MarkdownPlanLines
        => Set<Domain.Planning.MarkdownPlanLine>();

    /// <summary>Versioned bills of materials for manufactured and assembled items (Stage 16).</summary>
    public DbSet<Domain.Manufacturing.BillOfMaterials> BillOfMaterials
        => Set<Domain.Manufacturing.BillOfMaterials>();

    /// <summary>CRM leads. Stage 19.</summary>
    public DbSet<Domain.Crm.Lead> CrmLeads => Set<Domain.Crm.Lead>();

    /// <summary>CRM opportunities. Stage 19.</summary>
    public DbSet<Domain.Crm.Opportunity> CrmOpportunities => Set<Domain.Crm.Opportunity>();

    /// <summary>CRM activities. Stage 19.</summary>
    public DbSet<Domain.Crm.Activity> CrmActivities => Set<Domain.Crm.Activity>();

    /// <summary>CRM segments. Stage 19.</summary>
    public DbSet<Domain.Crm.Segment> CrmSegments => Set<Domain.Crm.Segment>();

    /// <summary>CRM static segment memberships. Stage 19.</summary>
    public DbSet<Domain.Crm.SegmentMember> CrmSegmentMembers => Set<Domain.Crm.SegmentMember>();

    /// <summary>CRM consent records. Stage 19.</summary>
    public DbSet<Domain.Crm.Consent> CrmConsents => Set<Domain.Crm.Consent>();

    /// <summary>Loyalty members. Stage 20.</summary>
    public DbSet<Domain.Loyalty.LoyaltyMember> LoyaltyMembers => Set<Domain.Loyalty.LoyaltyMember>();

    /// <summary>Loyalty transactions (Vuma-side event log). Stage 20.</summary>
    public DbSet<Domain.Loyalty.LoyaltyTransaction> LoyaltyTransactions
        => Set<Domain.Loyalty.LoyaltyTransaction>();

    /// <summary>Cached loyalty tiers. Stage 20.</summary>
    public DbSet<Domain.Loyalty.LoyaltyTier> LoyaltyTiers => Set<Domain.Loyalty.LoyaltyTier>();

    /// <summary>Cached loyalty rewards. Stage 20.</summary>
    public DbSet<Domain.Loyalty.LoyaltyReward> LoyaltyRewards => Set<Domain.Loyalty.LoyaltyReward>();

    /// <summary>Per-company loyalty settings. Stage 20.</summary>
    public DbSet<Domain.Loyalty.LoyaltySettings> LoyaltySettings => Set<Domain.Loyalty.LoyaltySettings>();

    /// <summary>
    /// The tenant the global query filter scopes to. Read through a context property rather than
    /// through the injected service directly, because that is the form EF Core recognises as a
    /// re-evaluated parameter instead of baking today's value into the cached compiled query.
    /// </summary>
    internal Guid CurrentTenantId => _tenantContext.TenantId;

    /// <summary>Whether the caller has opened an explicit cross-tenant scope. See <see cref="ITenantContext"/>.</summary>
    internal bool IsTenantFilterBypassed => _tenantContext.IsFilterBypassed;

    /// <summary>The active company, or null for legacy/bootstrap contexts.</summary>
    internal Guid? CurrentCompanyId => _companyContext?.CompanyId;

    /// <summary>
    /// Entities the company predicate never applies to. Licensing is per tenant by design — one
    /// licence, one subscription, one enforcement ladder (R9, ADR-028) — so a bound company must
    /// never hide the activation, licence, lease and metering rows the read-only guard reads.
    /// Without this, the first company-scoped write in any process answers 403 NotActivated
    /// against a fully current subscription, because the guard's own lookups come back empty.
    /// </summary>
    private static readonly HashSet<Type> CompanyFilterExemptions =
    [
        typeof(Domain.Licensing.Activation),
        typeof(Domain.Licensing.Licence),
        typeof(Domain.Licensing.Lease),
        typeof(Domain.Licensing.EmergencyUnlock),
        typeof(Domain.Licensing.TamperFlag),
        typeof(Domain.Licensing.ClockWatermark),
        typeof(Domain.Licensing.MeteringRecord),
        typeof(Domain.Licensing.SupportGrant),
        // Stage 10b: one tenant-wide customer-money policy row, read under any bound company.
        typeof(Domain.CustomerAccounts.CustomerFinanceTerms),
        // Stage 10b: tenant-wide finance configuration — the chart, the tax rules, the calendar
        // and the posting rules engine's own rules (all CloudToStore, all head-office data). A
        // company-bound handler that posts a journal reads every one of these on its way through
        // PostingRuleEngine; without this, the first bound write in any process answers
        // FINANCE_POSTING_RULE_NOT_FOUND against fully seeded rules, because the guard's own
        // lookups come back empty. Operational finance rows (journals, invoices, receipts,
        // bank accounts, counters) stay company-predicated: they are stamped per company at
        // write time, which is what keeps one company's books out of another's.
        typeof(Domain.Finance.Account),
        typeof(Domain.Finance.TaxRule),
        typeof(Domain.Finance.AccountingPeriod),
        typeof(Domain.Finance.PostingRule),
        typeof(Domain.Finance.PostingRuleLine),
        // The node-local document counters. One node owns one series (SYNC_AND_BACKUP.md §3):
        // a bound scope that cannot see the node's own counter re-creates it and dies on the
        // unique index instead of issuing the next number.
        typeof(Domain.Finance.DocumentNumberCounter),
    ];

    /// <inheritdoc />
    public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        => SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyCompanyIdentity();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyCompanyIdentity();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // A transaction already in flight means an outer scope owns the boundary; nesting a second
        // one here would either be ignored or commit half the outer scope's work.
        if (Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = await Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        TResult result = await operation(cancellationToken).ConfigureAwait(false);

        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        foreach (Assembly assembly in AdditionalModelAssemblies)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }

        ApplyGlobalQueryFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Assemblies beyond this one that contribute <c>IEntityTypeConfiguration</c> implementations.
    /// </summary>
    /// <remarks>
    /// Empty here. It is the seam a module extracted into its own assembly plugs into (ADR-010), and
    /// the seam the mapping-conformance tests use to map a probe entity through exactly the same base
    /// configuration as a real one — proving the conventions against real PostgreSQL without
    /// inventing a business table that nothing uses.
    /// </remarks>
    protected virtual IReadOnlyCollection<Assembly> AdditionalModelAssemblies => [];

    private void ApplyGlobalQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(Entity).IsAssignableFrom(entityType.ClrType) || entityType.BaseType is not null)
            {
                continue;
            }

            // Built through reflection because HasQueryFilter needs the filter typed to the entity,
            // and the whole point is that no entity gets to opt out by being configured by hand.
            // Licensing rows take the tenant-only shape: they are tenant-level by design, and the
            // company predicate would blind the enforcement guard under a bound company.
            string builder = CompanyFilterExemptions.Contains(entityType.ClrType)
                ? nameof(BuildTenantQueryFilter)
                : nameof(BuildQueryFilter);

            MethodInfo filter = typeof(VumaRetailDbContext)
                .GetMethod(builder, BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(entityType.ClrType);

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter((System.Linq.Expressions.LambdaExpression)filter.Invoke(this, null)!);
        }
    }

    private System.Linq.Expressions.Expression<Func<TEntity, bool>> BuildTenantQueryFilter<TEntity>()
        where TEntity : Entity
        // Soft delete (§7 rule 8) and tenant isolation, without the company predicate — for the
        // CompanyFilterExemptions set only. See the exemptions' own remarks for why licensing rows
        // must stay visible under a bound company.
        => entity => entity.DeletedAt == null
            && (IsTenantFilterBypassed || entity.TenantId == CurrentTenantId);

    private System.Linq.Expressions.Expression<Func<TEntity, bool>> BuildQueryFilter<TEntity>()
        where TEntity : Entity
        // Soft delete (§7 rule 8) and tenant isolation in one filter. CurrentTenantId is a context
        // property, so EF parameterises it per query rather than capturing the value at model build.
        // Guid.Empty means "no tenant resolved yet" — the activation wizard and the login screen —
        // and must not silently return every tenant's rows, so it matches nothing.
        => entity => entity.DeletedAt == null
            && (IsTenantFilterBypassed || entity.TenantId == CurrentTenantId)
            && (CurrentCompanyId == null || entity.CompanyId == CurrentCompanyId);

    private void ApplyCompanyIdentity()
    {
        Guid companyId = _companyContext?.CompanyId ?? Guid.Empty;

        foreach (var entry in ChangeTracker.Entries<Entity>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.State == EntityState.Added)
            {
                if (_companyContext?.CompanyId is { } activeCompany)
                    entry.Entity.AssignCompany(activeCompany);
                else if (entry.Entity.CompanyId is null)
                    entry.Entity.AssignCompany(companyId);
            }
            else if (_companyContext?.CompanyId is { } active && entry.Entity.CompanyId != active
                && !CompanyFilterExemptions.Contains(entry.Entity.GetType()))
            {
                throw new InvalidOperationException("A business row cannot be reassigned to another company.");
            }
        }
    }
}
