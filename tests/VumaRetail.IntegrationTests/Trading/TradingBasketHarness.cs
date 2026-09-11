using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Registry.Trading;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Sync;
using VumaRetail.Finance.Tax;
using VumaRetail.Infrastructure.Inventory;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Interceptors;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.Infrastructure.Sales;
using VumaRetail.Infrastructure.Sync;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Sync.Clock;

namespace VumaRetail.IntegrationTests.Trading;

/// <summary>
/// One tenant, two linked companies on two databases plus a registry database, one shared till,
/// routed items and real stock — the production topology, not an approximation of it.
/// </summary>
/// <remarks>
/// Stage 09b is the first place one human action writes two databases, so its tests run against
/// two databases: company separation is the connection string, exactly as production, and no
/// test can pass by leaning on a shared-database shortcut. The registry lives in a third
/// database holding companies, the SharedTill link and the routing index. Each database is a
/// cheap template clone.
/// </remarks>
public sealed class TradingBasketHarness : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _owned = [];
    private readonly ServiceProvider _scopes;

    private TradingBasketHarness(
        string registryConnection,
        string companyAConnection,
        string companyBConnection,
        VumaRegistryDbContext registry,
        TestClock clock,
        TestTenantContext tenant,
        TestPrincipalAccessor principal,
        Guid tenantId,
        Guid storeId,
        Guid companyAId,
        Guid companyBId,
        Guid operatorId,
        Guid premisesId,
        Guid terminalId,
        Guid cashierId,
        Guid hotPlateId,
        Guid glovesId,
        Guid maizeId,
        Guid customerId)
    {
        RegistryConnection = registryConnection;
        CompanyAConnection = companyAConnection;
        CompanyBConnection = companyBConnection;
        Registry = registry;
        Clock = clock;
        TenantContext = tenant;
        Principal = principal;
        TenantId = tenantId;
        StoreId = storeId;
        CompanyAId = companyAId;
        CompanyBId = companyBId;
        OperatorId = operatorId;
        PremisesId = premisesId;
        TerminalId = terminalId;
        CashierId = cashierId;
        HotPlateId = hotPlateId;
        GlovesId = glovesId;
        MaizeId = maizeId;
        CustomerId = customerId;

        Sessions = new TradingSessionRepository(registry);

        ServiceCollection scopes = new();
        scopes.AddLogging();
        scopes.AddSingleton<IClock>(clock);
        scopes.AddSingleton<IPrincipalAccessor>(principal);
        scopes.AddSingleton<IReplicationScope, Infrastructure.Sync.ReplicationScope>();
        scopes.AddScoped<ITenantContext>(_ => TestTenantContext.Unfiltered());
        scopes.AddScoped<ICompanyContext, AmbientCompanyContext>();
        scopes.AddScoped<AuditStamper>();
        scopes.AddScoped<ICompanyDbContextFactory>(provider => new TestCompanyFactory(
            new Dictionary<Guid, string>
            {
                [companyAId] = companyAConnection,
                [companyBId] = companyBConnection,
            },
            clock,
            principal,
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            Track));
        scopes.AddScoped<IReservationService>(provider => new ReservationService(
            provider.GetRequiredService<ICompanyDbContextFactory>(),
            clock,
            provider.GetRequiredService<AuditStamper>(),
            new ReplicationRegistry(OpenCompanyDb(companyAId)),
            new ReplicaWriter(OpenCompanyDb(companyAId), new Infrastructure.Sync.ReplicationScope()),
            new HybridLogicalClock(clock, StoreNode),
            StoreNode,
            NullLogger<ReservationService>.Instance));
        _scopes = scopes.BuildServiceProvider();
        _owned.Add(_scopes);
    }

    private static NodeIdentity StoreNode { get; } = new(
        new NodeIdentityOptions { NodeId = "store:harness", Kind = NodeKind.Store });

    /// <summary>The registry database connection string.</summary>
    public string RegistryConnection { get; }

    /// <summary>Noortgats Hardware's database connection string.</summary>
    public string CompanyAConnection { get; }

    /// <summary>Siyaya Cash and Carry's database connection string.</summary>
    public string CompanyBConnection { get; }

    /// <summary>The registry context under test.</summary>
    public VumaRegistryDbContext Registry { get; }

    /// <summary>The clock the test moves by hand.</summary>
    public TestClock Clock { get; }

    /// <summary>The tenant context.</summary>
    public TestTenantContext TenantContext { get; }

    /// <summary>The principal.</summary>
    public TestPrincipalAccessor Principal { get; }

    /// <summary>The seeded tenant.</summary>
    public Guid TenantId { get; }

    /// <summary>The seeded store.</summary>
    public Guid StoreId { get; }

    /// <summary>Noortgats Hardware: hot plates and gloves.</summary>
    public Guid CompanyAId { get; }

    /// <summary>Siyaya Cash and Carry: maize.</summary>
    public Guid CompanyBId { get; }

    /// <summary>The shared Operator ID.</summary>
    public Guid OperatorId { get; }

    /// <summary>The shared premises.</summary>
    public Guid PremisesId { get; }

    /// <summary>The shared terminal.</summary>
    public Guid TerminalId { get; }

    /// <summary>The cashier.</summary>
    public Guid CashierId { get; }

    /// <summary>2 × R799.00 hot plate (company A's catalogue).</summary>
    public Guid HotPlateId { get; }

    /// <summary>3 × R100.33 gloves (company A's catalogue).</summary>
    public Guid GlovesId { get; }

    /// <summary>1 × R214.00 maize 10kg (company B's catalogue).</summary>
    public Guid MaizeId { get; }

    /// <summary>The customer the basket is rung up against.</summary>
    public Guid CustomerId { get; }

    /// <summary>Trading-session aggregates.</summary>
    public TradingSessionRepository Sessions { get; }

    /// <summary>Creates a harness over three fresh databases.</summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public static async Task<TradingBasketHarness> CreateAsync(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        string registryConnection = await fixture.CreateDatabaseAsync().ConfigureAwait(false);
        string companyAConnection = await fixture.CreateDatabaseAsync().ConfigureAwait(false);
        string companyBConnection = await fixture.CreateDatabaseAsync().ConfigureAwait(false);

        var clock = new TestClock(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        var tenant = TestTenantContext.Unfiltered();
        var principal = new TestPrincipalAccessor("user:cashier");

        Guid companyAId = UuidV7.NewGuid();
        Guid companyBId = UuidV7.NewGuid();
        Guid operatorId = UuidV7.NewGuid();
        Guid premisesId = UuidV7.NewGuid();
        Guid terminalId = UuidV7.NewGuid();
        Guid cashierId = UuidV7.NewGuid();
        Guid customerId = UuidV7.NewGuid();

        CompanySeed seedA = await SeedCompanyAsync(
                companyAConnection, clock, principal, tenant,
                companyAId,
                [("HOTPLATE-2", "Hot plate 2-burner", 500m), ("GLOVE-WL", "Work gloves", 60m)])
            .ConfigureAwait(false);

        CompanySeed seedB = await SeedCompanyAsync(
                companyBConnection, clock, principal, tenant,
                companyBId,
                [("MAIZE-10KG", "Maize meal 10kg", 120m)],
                seedA.TenantId, seedA.StoreId)
            .ConfigureAwait(false);

        Guid tenantId = seedA.TenantId;
        Guid storeId = seedA.StoreId;
        Guid hotPlateId = seedA.ItemIds["HOTPLATE-2"];
        Guid glovesId = seedA.ItemIds["GLOVE-WL"];
        Guid maizeId = seedB.ItemIds["MAIZE-10KG"];

        await using (VumaRegistryDbContext registrySeed = TestDbContextFactory.ForRegistry(registryConnection, tenant))
        {
            Company companyA = Company.Create(tenantId, "NG", "Noortgats Hardware", "Noortgats Hardware", "ZAR", "en-ZA", "NG");
            companyA.AssignOperator(operatorId);
            companyA.SetConnectionSecretRef("test-secret-a");
            companyA.SetLifecycle(CompanyLifecycleState.Seeding);
            companyA.SetLifecycle(CompanyLifecycleState.Registered);
            companyA.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
            SetCompanyId(companyA, companyAId);
            registrySeed.Companies.Add(companyA);

            Company companyB = Company.Create(tenantId, "SC", "Siyaya Cash and Carry", "Siyaya Cash and Carry", "ZAR", "en-ZA", "SC");
            companyB.AssignOperator(operatorId);
            companyB.SetConnectionSecretRef("test-secret-b");
            companyB.SetLifecycle(CompanyLifecycleState.Seeding);
            companyB.SetLifecycle(CompanyLifecycleState.Registered);
            companyB.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
            SetCompanyId(companyB, companyBId);
            registrySeed.Companies.Add(companyB);

            CompanyLink link = CompanyLink.Create(
                tenantId, operatorId, companyAId, companyBId,
                CompanyLinkScope.SharedTill, clock.UtcNow);
            link.Accept(companyAId, "user:owner-a", "fp-a", clock.UtcNow);
            link.Accept(companyBId, "user:owner-b", "fp-b", clock.UtcNow);
            registrySeed.CompanyLinks.Add(link);

            registrySeed.CatalogRoutingIndex.Add(new CatalogRoutingIndexEntry
            {
                Id = UuidV7.NewGuid(),
                TenantId = tenantId,
                CompanyId = companyAId,
                CompanyCode = "NG",
                Barcode = "HOTPLATE-2",
                ItemId = hotPlateId,
                ItemCode = "HOTPLATE-2",
                Description = "Hot plate 2-burner",
                AsAt = clock.UtcNow,
            });
            registrySeed.CatalogRoutingIndex.Add(new CatalogRoutingIndexEntry
            {
                Id = UuidV7.NewGuid(),
                TenantId = tenantId,
                CompanyId = companyAId,
                CompanyCode = "NG",
                Barcode = "GLOVE-WL",
                ItemId = glovesId,
                ItemCode = "GLOVE-WL",
                Description = "Work gloves",
                AsAt = clock.UtcNow,
            });
            registrySeed.CatalogRoutingIndex.Add(new CatalogRoutingIndexEntry
            {
                Id = UuidV7.NewGuid(),
                TenantId = tenantId,
                CompanyId = companyBId,
                CompanyCode = "SC",
                Barcode = "MAIZE-10KG",
                ItemId = maizeId,
                ItemCode = "MAIZE-10KG",
                Description = "Maize meal 10kg",
                AsAt = clock.UtcNow,
            });

            await registrySeed.SaveChangesAsync().ConfigureAwait(false);
        }

        tenant.SetTenant(tenantId, storeId);
        tenant.EndBypass();

        VumaRegistryDbContext registry = TestDbContextFactory.ForRegistry(registryConnection, tenant);

        return new TradingBasketHarness(
            registryConnection, companyAConnection, companyBConnection, registry, clock, tenant, principal,
            tenantId, storeId, companyAId, companyBId, operatorId, premisesId, terminalId,
            cashierId, hotPlateId, glovesId, maizeId, customerId);
    }

    /// <summary>What one company's seed produced: the shared tenant/store ids and its item ids.</summary>
    private sealed record CompanySeed(Guid TenantId, Guid StoreId, IReadOnlyDictionary<string, Guid> ItemIds);

    /// <summary>Seeds one company's database: tenant, store, catalogue, tax, location, finance, stock.</summary>
    /// <remarks>
    /// Company A mints the shared tenant/store; company B joins them by id for every business
    /// row (its own Tenant/Store rows are unread ballast — nothing queries them, and there are
    /// no cross-table foreign keys to satisfy).
    /// </remarks>
    private static async Task<CompanySeed> SeedCompanyAsync(
        string connectionString,
        TestClock clock,
        TestPrincipalAccessor principal,
        TestTenantContext tenant,
        Guid companyId,
        IReadOnlyList<(string Code, string Name, decimal UnitCost)> items,
        Guid? sharedTenantId = null,
        Guid? sharedStoreId = null)
    {
        // Bound company context: rows the seed writes (including the poster's balances and
        // ledger entries) are stamped with this company at insert by ApplyCompanyIdentity —
        // post-hoc stamping provably does not stick, because the post belongs to a save that
        // already happened by the time the stamp runs.
        var boundCompany = new AmbientCompanyContext();
        boundCompany.SetCompany(companyId);
        await using VumaRetailDbContext seed = new(
            TestDbContextFactory.BuildOptions<VumaRetailDbContext>(connectionString, clock, principal),
            tenant,
            boundCompany);

        Tenant tenantRow = Tenant.CreateWithSouthAfricanDefaults("Trading Harness (Pty) Ltd", "Harness");
        tenantRow.Activate();
        Store storeRow = Store.Create(tenantRow.Id, "HSH01", "Harness shared floor");
        seed.Tenants.Add(tenantRow);
        seed.Stores.Add(storeRow);

        Guid tenantId = sharedTenantId ?? tenantRow.Id;
        Guid storeId = sharedStoreId ?? storeRow.Id;

        UnitOfMeasure each = UnitOfMeasure.CreateBase(tenantId, "EA", "Each", UnitOfMeasureType.Count);
        each.AssignCompany(companyId);
        seed.UnitsOfMeasure.Add(each);

        Dictionary<string, Guid> itemIds = [];
        foreach ((string code, string name, decimal unitCost) in items)
        {
            Item item = Item.Create(tenantId, code, name, ItemType.Stock, each.Id);
            item.AssignCompany(companyId);
            seed.Items.Add(item);
            itemIds[code] = item.Id;
        }

        seed.TaxRules.Add(TaxRule.Define(
            tenantId,
            "STANDARD",
            "South African VAT, standard rate",
            0.15m,
            TaxTreatment.Inclusive,
            DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddYears(-1)));

        StockLocation location = StockLocation.Create(
            tenantId, storeId, "FLOOR", "Sales floor", StockLocationType.SalesFloor);
        location.AssignCompany(companyId);
        seed.StockLocations.Add(location);

        SeedFinance(seed, tenantId, companyId, clock);

        await seed.CommitAsync().ConfigureAwait(false);

        StockLedgerPoster poster = new(
            new StockBalanceRepository(seed),
            new StockLedgerRepository(seed),
            new NullValuationEventPublisher(),
            clock);
        foreach ((string code, string name, decimal unitCost) in items)
        {
            Item item = await seed.Items.FirstAsync(candidate => candidate.Code == code).ConfigureAwait(false);
            await poster.ReceiveAsync(location, item.Id, null, new Quantity(100m, "EA"), new Money(unitCost, "ZAR"), "Opening stock");
        }

        await seed.CommitAsync().ConfigureAwait(false);
        return new CompanySeed(tenantId, storeId, itemIds);
    }

    private static void SeedFinance(VumaRetailDbContext seed, Guid tenantId, Guid companyId, TestClock clock)
    {
        Account debtors = Account.Open(tenantId, "1100", "Trade debtors", AccountType.Asset, "ZAR", ControlAccountType.AccountsReceivable);
        Account sales = Account.Open(tenantId, "4000", "Sales", AccountType.Revenue, "ZAR");
        Account salesReturns = Account.Open(tenantId, "4010", "Sales returns", AccountType.Revenue, "ZAR");
        Account vat = Account.Open(tenantId, "2200", "VAT control", AccountType.Liability, "ZAR");
        Account inventory = Account.Open(tenantId, "1300", "Inventory on hand", AccountType.Asset, "ZAR");
        Account costOfSales = Account.Open(tenantId, "5000", "Cost of sales", AccountType.Expense, "ZAR");
        Account bank = Account.Open(tenantId, "1000", "Till bank", AccountType.Asset, "ZAR");
        foreach (Account account in new[] { debtors, sales, salesReturns, vat, inventory, costOfSales, bank })
        {
            account.AssignCompany(companyId);
            seed.Accounts.Add(account);
        }

        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        AccountingPeriod period = AccountingPeriod.Open(
            tenantId, new DateOnly(today.Year, today.Month, 1), today.AddMonths(2));
        period.AssignCompany(companyId);
        seed.AccountingPeriods.Add(period);

        PostingRule saleTendered = PostingRule.Define(tenantId, "pos.sale.tendered", "Till sale");
        saleTendered.AssignCompany(companyId);
        saleTendered.AddLine(bank.Id, NormalBalance.Debit, "Gross");
        saleTendered.AddLine(sales.Id, NormalBalance.Credit, "Net");
        saleTendered.AddLine(vat.Id, NormalBalance.Credit, "Tax");
        seed.PostingRules.Add(saleTendered);

        PostingRule invoicePosted = PostingRule.Define(tenantId, "sales.invoice.posted", "Tax invoice");
        invoicePosted.AssignCompany(companyId);
        invoicePosted.AddLine(debtors.Id, NormalBalance.Debit, "Gross");
        invoicePosted.AddLine(sales.Id, NormalBalance.Credit, "Net");
        invoicePosted.AddLine(vat.Id, NormalBalance.Credit, "Tax");
        seed.PostingRules.Add(invoicePosted);

        PostingRule receiptSettled = PostingRule.Define(tenantId, "trading.session.receipt.settled", "Basket segment receipt");
        receiptSettled.AssignCompany(companyId);
        receiptSettled.AddLine(bank.Id, NormalBalance.Debit, "Principal");
        receiptSettled.AddLine(debtors.Id, NormalBalance.Credit, "Principal");
        seed.PostingRules.Add(receiptSettled);

        PostingRule returnCompleted = PostingRule.Define(tenantId, "sales.return.completed", "Sales return");
        returnCompleted.AssignCompany(companyId);
        returnCompleted.AddLine(salesReturns.Id, NormalBalance.Debit, "Net");
        returnCompleted.AddLine(vat.Id, NormalBalance.Debit, "Tax");
        returnCompleted.AddLine(debtors.Id, NormalBalance.Credit, "Gross");
        seed.PostingRules.Add(returnCompleted);

        PostingRule legReversed = PostingRule.Define(tenantId, "trading.session.leg-reversed", "Basket leg reversal");
        legReversed.AssignCompany(companyId);
        legReversed.AddLine(debtors.Id, NormalBalance.Debit, "Principal");
        legReversed.AddLine(bank.Id, NormalBalance.Credit, "Principal");
        seed.PostingRules.Add(legReversed);

        PostingRule issued = PostingRule.Define(tenantId, "inventory.sale.issued", "Stock issued");
        issued.AssignCompany(companyId);
        issued.AddLine(costOfSales.Id, NormalBalance.Debit, "Value");
        issued.AddLine(inventory.Id, NormalBalance.Credit, "Value");
        seed.PostingRules.Add(issued);

        PostingRule returned = PostingRule.Define(tenantId, "inventory.sale.returned", "Stock returned");
        returned.AssignCompany(companyId);
        returned.AddLine(inventory.Id, NormalBalance.Debit, "Value");
        returned.AddLine(costOfSales.Id, NormalBalance.Credit, "Value");
        seed.PostingRules.Add(returned);

        foreach (PostingRule rule in seed.PostingRules.Local.ToList())
        {
            foreach (PostingRuleLine line in rule.Lines)
            {
                line.AssignCompany(companyId);
            }
        }
    }

    private static void SetCompanyId(Company company, Guid companyId)
    {
        // Company ids are minted inside Create; the harness needs predetermined ids so both
        // databases and the registry agree before any of them is seeded. The property setter
        // is private by design — this is the one place allowed to override it, with the test's
        // own deterministic id, and the registry seed asserts the pair afterwards.
        var property = typeof(Company).GetProperty("Id")
            ?? throw new InvalidOperationException("Company has no Id.");
        property.SetValue(company, companyId);
    }

    /// <summary>Builds the real completion saga over this harness's databases.</summary>
    /// <param name="links">The company-link guard. Omit for the real guard over seeded links.</param>
    public MixedBasketCompletionService CreateCompletionService(ICompanyLinkService? links = null)
    {
        ICompanyLinkService guard = links
            ?? new CompanyLinkService(Registry, Clock, TenantContext, Substitute.For<IOperatorContext>());

        return new MixedBasketCompletionService(
            Registry,
            Sessions,
            new TradingCompanyGateway(_scopes.GetRequiredService<IServiceScopeFactory>()),
            _scopes.GetRequiredService<IServiceScopeFactory>(),
            guard,
            new ReplicationRegistry(OpenCompanyDb(CompanyAId)),
            new ReplicaWriter(OpenCompanyDb(CompanyAId), new Infrastructure.Sync.ReplicationScope()),
            new HybridLogicalClock(Clock, StoreNode),
            StoreNode,
            Clock,
            NullLogger<MixedBasketCompletionService>.Instance);
    }

    /// <summary>Builds the real return facade over this harness's databases.</summary>
    public MixedBasketReturnService CreateReturnService()
        => new(
            Sessions,
            new TradingCompanyGateway(_scopes.GetRequiredService<IServiceScopeFactory>()),
            _scopes.GetRequiredService<IServiceScopeFactory>(),
            new ReplicationRegistry(OpenCompanyDb(CompanyAId)),
            new ReplicaWriter(OpenCompanyDb(CompanyAId), new Infrastructure.Sync.ReplicationScope()),
            new HybridLogicalClock(Clock, StoreNode),
            StoreNode,
            Clock,
            NullLogger<MixedBasketReturnService>.Instance);

    /// <summary>Builds real trading-session command handlers (no dispatcher: call then save).</summary>
    public TradingHandlers Handlers()
    {
        VumaRetailDbContext companyA = OpenCompanyDb(CompanyAId);
        var tax = new TaxEngine(new TaxRuleRepository(companyA));
        var packs = new PackSizeResolver(new UnitOfMeasureRepository(companyA));
        var barcodes = new BarcodeResolver(
            Registry,
            Substitute.For<ICompanyContext>(),
            Substitute.For<ICompanyDbContextFactory>(),
            Clock);
        var links = new CompanyLinkService(Registry, Clock, TenantContext, Substitute.For<IOperatorContext>());
        return new TradingHandlers(this, tax, packs, barcodes, links);
    }

    /// <summary>Opens a context over one company's database.</summary>
    public VumaRetailDbContext OpenCompanyDb(Guid companyId)
    {
        string connection = companyId == CompanyAId ? CompanyAConnection
            : companyId == CompanyBId ? CompanyBConnection
            : throw new ArgumentException("Unknown test company.", nameof(companyId));
        VumaRetailDbContext context = TestDbContextFactory.For(connection, Clock, Principal, TenantContext);
        Track(context);
        return context;
    }

    /// <summary>Saves session changes, like the pipeline's unit of work would.</summary>
    public async Task SaveSessionsAsync(CancellationToken cancellationToken = default)
    {
        // Both chains: handlers mint document numbers through the session company's database
        // while the session rows land in the registry — the pipeline commits both.
        await using VumaRetailDbContext companyA = TestDbContextFactory.For(CompanyAConnection, Clock, Principal, TenantContext);
        await companyA.CommitAsync(cancellationToken).ConfigureAwait(false);
        await Registry.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Track(VumaRetailDbContext context)
    {
        _owned.Add(context);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        for (int index = _owned.Count - 1; index >= 0; index--)
        {
            await _owned[index].DisposeAsync().ConfigureAwait(false);
        }

        await Registry.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Command handlers bound to this harness's contexts.</summary>
    public sealed class TradingHandlers(TradingBasketHarness harness, TaxEngine tax, PackSizeResolver packs, BarcodeResolver barcodes, CompanyLinkService links)
    {

        /// <summary>Runs a command, then commits the session — the pipeline's unit of work, spelled out.</summary>
        public async Task<Guid> OpenAsync(OpenTradingSessionCommand command, CancellationToken cancellationToken = default)
        {
            await using VumaRetailDbContext companyA = TestDbContextFactory.For(
                harness.CompanyAConnection, harness.Clock, harness.Principal, harness.TenantContext);
            var handler = new OpenTradingSessionCommandHandler(
                harness.Sessions,
                new DocumentNumberSequence(companyA, harness.TenantContext),
                harness.TenantContext,
                harness.Clock);
            Guid id = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
            await companyA.CommitAsync(cancellationToken).ConfigureAwait(false);
            await harness.SaveSessionsAsync(cancellationToken).ConfigureAwait(false);
            return id;
        }

        /// <summary>Runs an add-line, then commits.</summary>
        public async Task<Guid> AddLineAsync(AddBasketLineCommand command, CancellationToken cancellationToken = default)
        {
            var handler = new AddBasketLineCommandHandler(
                harness.Sessions, barcodes, links, tax, packs, harness.TenantContext, harness.Clock);
            Guid id = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
            await harness.SaveSessionsAsync(cancellationToken).ConfigureAwait(false);
            return id;
        }

        /// <summary>Runs a tender capture, then commits.</summary>
        public async Task CaptureTenderAsync(CaptureTenderCommand command, CancellationToken cancellationToken = default)
        {
            var handler = new CaptureTenderCommandHandler(harness.Sessions, harness.TenantContext, harness.Clock);
            await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
            await harness.SaveSessionsAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Runs an allocation override, then commits.</summary>
        public async Task OverrideAllocationAsync(OverrideTenderAllocationCommand command, CancellationToken cancellationToken = default)
        {
            var handler = new OverrideTenderAllocationCommandHandler(harness.Sessions, harness.TenantContext);
            await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
            await harness.SaveSessionsAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Runs a session void, then commits.</summary>
        public async Task VoidSessionAsync(VoidTradingSessionCommand command, CancellationToken cancellationToken = default)
        {
            var handler = new VoidTradingSessionCommandHandler(harness.Sessions, harness.TenantContext, harness.Clock);
            await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
            await harness.SaveSessionsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class TestCompanyFactory(
        IReadOnlyDictionary<Guid, string> connections,
        TestClock clock,
        TestPrincipalAccessor principal,
        ITenantContext tenant,
        ICompanyContext company,
        Action<VumaRetailDbContext> track) : ICompanyDbContextFactory
    {
        public Task<VumaRetailDbContext> CreateAsync(CancellationToken cancellationToken = default)
            => CreateAsync(CompanyAccessMode.Write, cancellationToken);

        public Task<VumaRetailDbContext> CreateAsync(CompanyAccessMode access, CancellationToken cancellationToken = default)
        {
            Guid companyId = company.CompanyId
                ?? throw new InvalidOperationException("A leg needs its company bound before it opens a database.");

            if (!connections.TryGetValue(companyId, out string? connectionString))
            {
                throw new InvalidOperationException($"No test database for company {companyId}.");
            }

            VumaRetailDbContext context = new(
                TestDbContextFactory.BuildOptions<VumaRetailDbContext>(connectionString, clock, principal),
                tenant,
                company);
            track(context);
            return Task.FromResult(context);
        }
    }

    private sealed class NullValuationEventPublisher : IInventoryValuationEventPublisher
    {
        public Task PublishAsync(InventoryValuationEvent valuationEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
