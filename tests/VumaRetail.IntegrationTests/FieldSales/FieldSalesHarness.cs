using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.FieldSales.Commands;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.FieldSales;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Sales;
using VumaRetail.Domain.Sync;
using VumaRetail.Domain.Workflow;
using VumaRetail.Finance.Tax;
using VumaRetail.Infrastructure.FieldSales;
using VumaRetail.Infrastructure.Inventory;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Interceptors;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.Infrastructure.Sales;
using VumaRetail.Infrastructure.Sync;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Sync.Clock;
using VumaRetail.Workflow.Approvals;
using VumaRetail.Workflow.Approvals;

namespace VumaRetail.IntegrationTests.FieldSales;

/// <summary>
/// One tenant, two linked companies on two databases plus a registry database: reps, routed
/// stock, price lists, an approval policy, a manager, a credit group and a live availability
/// projection — the production topology, like the trading-basket harness.
/// </summary>
public sealed class FieldSalesHarness : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _owned = [];
    private readonly ServiceProvider _scopes;

    private FieldSalesHarness(
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
        Guid managerId,
        Guid customerId,
        Guid hotPlateAId,
        Guid glovesAId,
        Guid maizeBId,
        Guid creditGroupId)
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
        ManagerId = managerId;
        CustomerId = customerId;
        HotPlateAId = hotPlateAId;
        GlovesAId = glovesAId;
        MaizeBId = maizeBId;
        CreditGroupId = creditGroupId;

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
        scopes.AddScoped<ISourcingPlanner>(_ => new SourcingPlanner(
            new GroupProjectionAvailability(registry, clock),
            new AvailabilityThenProximityStrategy()));
        scopes.AddScoped<ITradingCompanyGateway, TradingCompanyGateway>();
        _scopes = scopes.BuildServiceProvider();
        _owned.Add(_scopes);
    }

    private static NodeIdentity StoreNode { get; } = new(
        new NodeIdentityOptions { NodeId = "store:harness", Kind = NodeKind.Store });

    /// <summary>The registry database connection string.</summary>
    public string RegistryConnection { get; }

    /// <summary>Company A's database connection string.</summary>
    public string CompanyAConnection { get; }

    /// <summary>Company B's database connection string.</summary>
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

    /// <summary>The ordering company.</summary>
    public Guid CompanyAId { get; }

    /// <summary>The sister company.</summary>
    public Guid CompanyBId { get; }

    /// <summary>The shared Operator ID.</summary>
    public Guid OperatorId { get; }

    /// <summary>The manager who approves (holds fieldsales.proforma.approve).</summary>
    public Guid ManagerId { get; }

    /// <summary>The customer reps quote.</summary>
    public Guid CustomerId { get; }

    /// <summary>A till terminal id for seeded sales.</summary>
    public Guid TerminalId { get; } = UuidV7.NewGuid();

    /// <summary>Hot plate in company A's catalogue.</summary>
    public Guid HotPlateAId { get; }

    /// <summary>Gloves in company A's catalogue.</summary>
    public Guid GlovesAId { get; }

    /// <summary>Maize in company B's catalogue.</summary>
    public Guid MaizeBId { get; }

    /// <summary>The credit group spanning both companies.</summary>
    public Guid CreditGroupId { get; }

    /// <summary>Creates a harness over three fresh databases.</summary>
    public static async Task<FieldSalesHarness> CreateAsync(PostgresFixture fixture, decimal groupLimit = 100000m)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        string registryConnection = await fixture.CreateDatabaseAsync().ConfigureAwait(false);
        string companyAConnection = await fixture.CreateDatabaseAsync().ConfigureAwait(false);
        string companyBConnection = await fixture.CreateDatabaseAsync().ConfigureAwait(false);

        var clock = new TestClock(new DateTimeOffset(2026, 9, 9, 8, 0, 0, TimeSpan.Zero));
        var tenant = TestTenantContext.Unfiltered();
        var principal = new TestPrincipalAccessor("user:rep");

        Guid companyAId = UuidV7.NewGuid();
        Guid companyBId = UuidV7.NewGuid();
        Guid operatorId = UuidV7.NewGuid();
        Guid managerId = UuidV7.NewGuid();
        Guid customerId = UuidV7.NewGuid();

        CompanySeed seedA = await SeedCompanyAsync(
                companyAConnection, clock, principal, tenant, companyAId, managerId,
                [("HOTPLATE-2", "Hot plate 2-burner", 500m, 799.00m), ("GLOVE-WL", "Work gloves", 60m, 100.33m)])
            .ConfigureAwait(false);

        CompanySeed seedB = await SeedCompanyAsync(
                companyBConnection, clock, principal, tenant, companyBId, managerId,
                [("MAIZE-10KG", "Maize meal 10kg", 120m, 214.00m)],
                seedA.TenantId, seedA.StoreId)
            .ConfigureAwait(false);

        Guid tenantId = seedA.TenantId;
        Guid storeId = seedA.StoreId;

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

            // One row per pair (unique on tenant + ordered pair): scopes combine as flags.
            CompanyLink link = CompanyLink.Create(
                tenantId, operatorId, companyAId, companyBId,
                CompanyLinkScope.SharedSourcing | CompanyLinkScope.SharedCredit, clock.UtcNow);
            link.Accept(companyAId, "user:owner-a", "fp-a", clock.UtcNow);
            link.Accept(companyBId, "user:owner-b", "fp-b", clock.UtcNow);
            registrySeed.CompanyLinks.Add(link);

            CreditGroup group = new(tenantId, "Rep book", "Receivable", groupLimit, "ZAR");
            group.Members.Add(new CreditGroupMember(group.Id, companyAId, null, tenantId));
            group.Members.Add(new CreditGroupMember(group.Id, companyBId, null, tenantId));
            registrySeed.CreditGroups.Add(group);

            registrySeed.GroupAvailabilityRows.Add(GroupAvailabilityRow.Publish(
                tenantId, companyAId, "NG", seedA.LocationId, seedA.ItemIds["HOTPLATE-2"], null,
                100m, 0m, 0m, "EA", clock.UtcNow));
            registrySeed.GroupAvailabilityRows.Add(GroupAvailabilityRow.Publish(
                tenantId, companyAId, "NG", seedA.LocationId, seedA.ItemIds["GLOVE-WL"], null,
                100m, 0m, 0m, "EA", clock.UtcNow));
            registrySeed.GroupAvailabilityRows.Add(GroupAvailabilityRow.Publish(
                tenantId, companyBId, "SC", seedB.LocationId, seedB.ItemIds["MAIZE-10KG"], null,
                100m, 0m, 0m, "EA", clock.UtcNow));

            await registrySeed.SaveChangesAsync().ConfigureAwait(false);

            // The group id is minted inside Create; read it back for the tests.
            Guid groupId = group.Id;

            tenant.SetTenant(tenantId, storeId);
            tenant.EndBypass();

            VumaRegistryDbContext registry = TestDbContextFactory.ForRegistry(registryConnection, tenant);

            return new FieldSalesHarness(
                registryConnection, companyAConnection, companyBConnection, registry, clock, tenant, principal,
                tenantId, storeId, companyAId, companyBId, operatorId, managerId, customerId,
                seedA.ItemIds["HOTPLATE-2"], seedA.ItemIds["GLOVE-WL"], seedB.ItemIds["MAIZE-10KG"], groupId);
        }
    }

    private sealed record CompanySeed(
        Guid TenantId, Guid StoreId, Guid LocationId, IReadOnlyDictionary<string, Guid> ItemIds);

    private static async Task<CompanySeed> SeedCompanyAsync(
        string connectionString,
        TestClock clock,
        TestPrincipalAccessor principal,
        TestTenantContext tenant,
        Guid companyId,
        Guid managerId,
        IReadOnlyList<(string Code, string Name, decimal UnitCost, decimal ShelfPrice)> items,
        Guid? sharedTenantId = null,
        Guid? sharedStoreId = null)
    {
        var boundCompany = new AmbientCompanyContext();
        boundCompany.SetCompany(companyId);
        await using VumaRetailDbContext seed = new(
            TestDbContextFactory.BuildOptions<VumaRetailDbContext>(connectionString, clock, principal),
            tenant,
            boundCompany);

        Tenant tenantRow = Tenant.CreateWithSouthAfricanDefaults("Field Sales Harness (Pty) Ltd", "Harness");
        tenantRow.Activate();
        Store storeRow = Store.Create(tenantRow.Id, "HSH01", "Harness shared floor");
        seed.Tenants.Add(tenantRow);
        seed.Stores.Add(storeRow);

        Guid tenantId = sharedTenantId ?? tenantRow.Id;
        Guid storeId = sharedStoreId ?? storeRow.Id;

        UnitOfMeasure each = UnitOfMeasure.CreateBase(tenantId, "EA", "Each", UnitOfMeasureType.Count);
        seed.UnitsOfMeasure.Add(each);

        Dictionary<string, Guid> itemIds = [];
        foreach ((string code, string name, decimal unitCost, decimal shelfPrice) in items)
        {
            Item item = Item.Create(tenantId, code, name, ItemType.Stock, each.Id);
            seed.Items.Add(item);
            itemIds[code] = item.Id;
        }

        seed.TaxRules.Add(TaxRule.Define(
            tenantId, "STANDARD", "South African VAT, standard rate", 0.15m,
            TaxTreatment.Inclusive, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddYears(-1)));

        StockLocation location = StockLocation.Create(
            tenantId, storeId, "FLOOR", "Sales floor", StockLocationType.SalesFloor);
        seed.StockLocations.Add(location);

        PriceList retail = PriceList.Create(
            tenantId, storeId, "RETAIL", "Retail", "ZAR", PriceListKind.Retail,
            pricesIncludeTax: true, priority: 10,
            DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddYears(-1), null);
        seed.PriceLists.Add(retail);

        SeedFinance(seed, tenantId, clock);
        SeedApproval(seed, tenantId, managerId, clock);

        await seed.CommitAsync().ConfigureAwait(false);

        StockLedgerPoster poster = new(
            new StockBalanceRepository(seed),
            new StockLedgerRepository(seed),
            new NullValuationEventPublisher(),
            clock);
        foreach ((string code, string name, decimal unitCost, decimal shelfPrice) in items)
        {
            Item item = await seed.Items.FirstAsync(candidate => candidate.Code == code).ConfigureAwait(false);
            seed.PriceListLines.Add(PriceListLine.Create(
                tenantId, storeId, retail.Id, item.Id, null, new Money(shelfPrice, "ZAR"), minimumQuantity: 1m));
            await poster.ReceiveAsync(location, item.Id, null, new Quantity(100m, "EA"), new Money(unitCost, "ZAR"), "Opening stock");
        }

        await seed.CommitAsync().ConfigureAwait(false);
        return new CompanySeed(tenantId, storeId, location.Id, itemIds);
    }

    private static void SeedFinance(VumaRetailDbContext seed, Guid tenantId, TestClock clock)
    {
        Account debtors = Account.Open(tenantId, "1100", "Trade debtors", AccountType.Asset, "ZAR", ControlAccountType.AccountsReceivable);
        Account sales = Account.Open(tenantId, "4000", "Sales", AccountType.Revenue, "ZAR");
        Account salesReturns = Account.Open(tenantId, "4010", "Sales returns", AccountType.Revenue, "ZAR");
        Account vat = Account.Open(tenantId, "2200", "VAT control", AccountType.Liability, "ZAR");
        Account inventory = Account.Open(tenantId, "1300", "Inventory on hand", AccountType.Asset, "ZAR");
        Account costOfSales = Account.Open(tenantId, "5000", "Cost of sales", AccountType.Expense, "ZAR");
        Account bank = Account.Open(tenantId, "1000", "Till bank", AccountType.Asset, "ZAR");
        seed.Accounts.Add(debtors);
        seed.Accounts.Add(sales);
        seed.Accounts.Add(salesReturns);
        seed.Accounts.Add(vat);
        seed.Accounts.Add(inventory);
        seed.Accounts.Add(costOfSales);
        seed.Accounts.Add(bank);

        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        seed.AccountingPeriods.Add(AccountingPeriod.Open(
            tenantId, new DateOnly(today.Year, today.Month, 1), today.AddMonths(2)));

        PostingRule invoicePosted = PostingRule.Define(tenantId, "sales.invoice.posted", "Tax invoice");
        invoicePosted.AddLine(debtors.Id, NormalBalance.Debit, "Gross");
        invoicePosted.AddLine(sales.Id, NormalBalance.Credit, "Net");
        invoicePosted.AddLine(vat.Id, NormalBalance.Credit, "Tax");
        seed.PostingRules.Add(invoicePosted);

        PostingRule issued = PostingRule.Define(tenantId, "inventory.sale.issued", "Stock issued");
        issued.AddLine(costOfSales.Id, NormalBalance.Debit, "Value");
        issued.AddLine(inventory.Id, NormalBalance.Credit, "Value");
        seed.PostingRules.Add(issued);

        PostingRule returned = PostingRule.Define(tenantId, "inventory.sale.returned", "Stock returned");
        returned.AddLine(inventory.Id, NormalBalance.Debit, "Value");
        returned.AddLine(costOfSales.Id, NormalBalance.Credit, "Value");
        seed.PostingRules.Add(returned);

        PostingRule returnCompleted = PostingRule.Define(tenantId, "sales.return.completed", "Sales return");
        returnCompleted.AddLine(salesReturns.Id, NormalBalance.Debit, "Net");
        returnCompleted.AddLine(vat.Id, NormalBalance.Debit, "Tax");
        returnCompleted.AddLine(debtors.Id, NormalBalance.Credit, "Gross");
        seed.PostingRules.Add(returnCompleted);
    }

    private static void SeedApproval(VumaRetailDbContext seed, Guid tenantId, Guid managerId, TestClock clock)
    {
        // The default field-sales policy: every rep pro forma requires approval, whatever its value.
        seed.ApprovalPolicies.Add(ApprovalPolicy.Define(
            tenantId, "field-sales", "ProFormaOrder", "Approve",
            "fieldsales.proforma.approve", new Money(0m, "ZAR")));
        seed.ApprovalPolicies.Add(ApprovalPolicy.Define(
            tenantId, "field-sales", "ProFormaCreditNote", "Approve",
            "fieldsales.proforma.approve", new Money(0m, "ZAR")));

        Role managers = Role.Create(tenantId, "Sales Managers");
        seed.Roles.Add(managers);
        seed.RolePermissions.Add(RolePermission.Grant(
            tenantId, managers.Id, PermissionKey.Parse("fieldsales.proforma.approve")));

        User manager = User.Create(tenantId, "manager", "Mpho Manager");
        SetUserId(manager, managerId);
        seed.Users.Add(manager);
        seed.UserRoleAssignments.Add(UserRoleAssignment.Assign(tenantId, managerId, managers.Id));
    }

    private static void SetCompanyId(Company company, Guid companyId)
    {
        var property = typeof(Company).GetProperty("Id")
            ?? throw new InvalidOperationException("Company has no Id.");
        property.SetValue(company, companyId);
    }

    private static void SetUserId(User user, Guid userId)
    {
        var property = typeof(User).GetProperty("Id")
            ?? throw new InvalidOperationException("User has no Id.");
        property.SetValue(user, userId);
    }

    /// <summary>Builds the real approval saga over this harness's databases.</summary>
    public FieldSalesApprovalService CreateApprovalService()
    {
        var links = new CompanyLinkService(Registry, Clock, TenantContext, Substitute.For<IOperatorContext>());
        var credit = new GroupCreditService(Registry, Clock, links);

        return new FieldSalesApprovalService(
            Registry,
            _scopes.GetRequiredService<IServiceScopeFactory>(),
            new CompanyLinkGuard(links),
            credit,
            new ReplicationRegistry(OpenCompanyDb(CompanyAId)),
            new ReplicaWriter(OpenCompanyDb(CompanyAId), new Infrastructure.Sync.ReplicationScope()),
            new HybridLogicalClock(Clock, StoreNode),
            StoreNode,
            Clock,
            NullLogger<FieldSalesApprovalService>.Instance);
    }

    /// <summary>Builds a real approval engine over one company's database, acting as one principal.</summary>
    /// <remarks>
    /// The caller passes its own context and commits it: the engine's request rows must land in
    /// the same transaction boundary the test commits, not in a private context nobody saves.
    /// </remarks>
    public ApprovalEngine CreateApprovalEngine(
        VumaRetailDbContext companyDb, string principal)
    {
        return new ApprovalEngine(
            new ApprovalPolicyRepository(companyDb),
            new ApprovalRequestRepository(companyDb),
            new ApprovalDecisionRepository(companyDb),
            new RoleRepository(companyDb),
            new TestPrincipalAccessor(principal),
            TenantContext,
            Clock);
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

    /// <summary>Group projection reads for the planner: the real reader, nothing stubbed.</summary>
    private sealed class GroupProjectionAvailability(VumaRegistryDbContext registry, IClock clock) : IAvailabilityService
    {
        private readonly RegistryAvailabilityReader _reader = new(registry, clock);

        public Task<GroupAvailabilityView> GetGroupAsync(Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default)
            => _reader.ReadAsync(itemId, itemVariantId, TimeSpan.FromMinutes(15), cancellationToken);

        public Task<LocalAvailability> GetLocalAsync(Guid locationId, Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The planner reads group availability only.");
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
