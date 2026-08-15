using System.Reflection;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
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

    /// <summary>Creates the context.</summary>
    /// <param name="options">EF options, including the Npgsql provider and the interceptors.</param>
    /// <param name="tenantContext">Supplies the tenant the global query filter scopes to.</param>
    public VumaRetailDbContext(DbContextOptions options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
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

    /// <summary>
    /// The tenant the global query filter scopes to. Read through a context property rather than
    /// through the injected service directly, because that is the form EF Core recognises as a
    /// re-evaluated parameter instead of baking today's value into the cached compiled query.
    /// </summary>
    internal Guid CurrentTenantId => _tenantContext.TenantId;

    /// <summary>Whether the caller has opened an explicit cross-tenant scope. See <see cref="ITenantContext"/>.</summary>
    internal bool IsTenantFilterBypassed => _tenantContext.IsFilterBypassed;

    /// <inheritdoc />
    public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        => SaveChangesAsync(cancellationToken);

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
            MethodInfo filter = typeof(VumaRetailDbContext)
                .GetMethod(nameof(BuildQueryFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(entityType.ClrType);

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter((System.Linq.Expressions.LambdaExpression)filter.Invoke(this, null)!);
        }
    }

    private System.Linq.Expressions.Expression<Func<TEntity, bool>> BuildQueryFilter<TEntity>()
        where TEntity : Entity
        // Soft delete (§7 rule 8) and tenant isolation in one filter. CurrentTenantId is a context
        // property, so EF parameterises it per query rather than capturing the value at model build.
        // Guid.Empty means "no tenant resolved yet" — the activation wizard and the login screen —
        // and must not silently return every tenant's rows, so it matches nothing.
        => entity => entity.DeletedAt == null
            && (IsTenantFilterBypassed || entity.TenantId == CurrentTenantId);
}
