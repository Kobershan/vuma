using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Sync;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Interceptors;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.Infrastructure.Sync;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Sync.Clock;

namespace VumaRetail.IntegrationTests.Registry;

/// <summary>
/// One tenant, three companies sharing one Operator ID, one customer and one bank account over a
/// real database, with the Stage 07c receipt saga wired to take real serialisable transactions
/// per company.
/// </summary>
/// <remarks>
/// The companies and the registry share one test database (the template migrates both chains into
/// it). That is a test convenience, not the topology: the service under test never assumes it —
/// it opens each company's context through <c>ICompanyDbContextFactory</c> in a child scope and
/// publishes to an explicitly handed registry context, exactly as production does across
/// databases. Company separation inside the shared database is the <c>CompanyId</c> column, which
/// every receipt, invoice and journal line carries. One company's database "going down" is the
/// factory refusing to open its context, which is exactly what the production serving guard does
/// before a connection is ever attempted.
/// </remarks>
public sealed class GroupReceiptHarness : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _owned = [];
    private readonly ServiceProvider _scopes;
    private readonly HashSet<Guid> _downCompanies = [];

    private GroupReceiptHarness(
        string connectionString,
        VumaRetailDbContext context,
        VumaRegistryDbContext registry,
        TestClock clock,
        TestTenantContext tenant,
        TestPrincipalAccessor principal,
        ICompanyLinkGuard links,
        Guid tenantId,
        Guid storeId,
        Guid companyAId,
        Guid companyBId,
        Guid companyCId,
        Guid operatorId,
        Guid customerId,
        Guid bankAccountId,
        Guid invoiceAId,
        Guid invoiceBId,
        Guid invoiceCId)
    {
        ConnectionString = connectionString;
        Context = context;
        Registry = registry;
        Clock = clock;
        TenantContext = tenant;
        Principal = principal;
        TenantId = tenantId;
        StoreId = storeId;
        CompanyAId = companyAId;
        CompanyBId = companyBId;
        CompanyCId = companyCId;
        OperatorId = operatorId;
        CustomerId = customerId;
        BankAccountId = bankAccountId;
        InvoiceAId = invoiceAId;
        InvoiceBId = invoiceBId;
        InvoiceCId = invoiceCId;

        ServiceCollection scopes = new();
        scopes.AddSingleton<IClock>(clock);
        scopes.AddSingleton<IPrincipalAccessor>(principal);
        scopes.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        scopes.AddSingleton<ILogger<GroupReceiptLegDispatcher>>(NullLogger<GroupReceiptLegDispatcher>.Instance);
        scopes.AddSingleton<ILogger<GroupReceiptService>>(NullLogger<GroupReceiptService>.Instance);
        scopes.AddSingleton<IReplicationScope, ReplicationScope>();
        scopes.AddScoped<ITenantContext>(_ => TestTenantContext.Unfiltered());
        scopes.AddScoped<ICompanyContext, AmbientCompanyContext>();
        scopes.AddScoped<AuditStamper>();
        scopes.AddScoped<ICompanyDbContextFactory>(provider => new OutageCompanyFactory(
            connectionString,
            clock,
            principal,
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            _downCompanies,
            Track));
        _scopes = scopes.BuildServiceProvider();
        _owned.Add(_scopes);

        Links = links;
        Service = CreateService(links);
        Repository = new GroupReceiptRepository(registry);
    }

    /// <summary>The connection string, for opening further contexts.</summary>
    public string ConnectionString { get; }

    /// <summary>The company database context used for seeding and assertions.</summary>
    public VumaRetailDbContext Context { get; }

    /// <summary>The registry context used for seeding and assertions.</summary>
    public VumaRegistryDbContext Registry { get; }

    /// <summary>The clock the test moves by hand.</summary>
    public TestClock Clock { get; }

    /// <summary>The tenant context.</summary>
    public TestTenantContext TenantContext { get; }

    /// <summary>The principal.</summary>
    public TestPrincipalAccessor Principal { get; }

    /// <summary>The company-link guard the service enforces (a substitute by default).</summary>
    public ICompanyLinkGuard Links { get; }

    /// <summary>The service under test.</summary>
    public IGroupReceiptService Service { get; }

    /// <summary>The registry repository, for assertions.</summary>
    public IGroupReceiptRepository Repository { get; }

    /// <summary>The seeded tenant.</summary>
    public Guid TenantId { get; }

    /// <summary>The seeded store.</summary>
    public Guid StoreId { get; }

    /// <summary>The bank-owning capturing company.</summary>
    public Guid CompanyAId { get; }

    /// <summary>The second company (R3 000 share).</summary>
    public Guid CompanyBId { get; }

    /// <summary>The third company (R5 000 share).</summary>
    public Guid CompanyCId { get; }

    /// <summary>The Operator ID all three companies share.</summary>
    public Guid OperatorId { get; }

    /// <summary>The shared customer every allocation is receipted against.</summary>
    public Guid CustomerId { get; }

    /// <summary>The receiving bank account (owned by company A).</summary>
    public Guid BankAccountId { get; }

    /// <summary>Company A's open invoice (R1 000 outstanding).</summary>
    public Guid InvoiceAId { get; }

    /// <summary>Company B's open invoice (R3 000 outstanding).</summary>
    public Guid InvoiceBId { get; }

    /// <summary>Company C's open invoice (R5 000 outstanding).</summary>
    public Guid InvoiceCId { get; }

    /// <summary>Creates a harness over a fresh database.</summary>
    public static async Task<GroupReceiptHarness> CreateAsync(
        PostgresFixture fixture, ICompanyLinkGuard? links = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        string connectionString = await fixture.CreateDatabaseAsync().ConfigureAwait(false);

        var clock = new TestClock();
        var tenant = TestTenantContext.Unfiltered();
        var principal = new TestPrincipalAccessor("user:ayanda");
        ICompanyLinkGuard guard = links ?? Substitute.For<ICompanyLinkGuard>();

        Guid tenantId;
        Guid storeId;
        Guid companyAId;
        Guid companyBId;
        Guid companyCId;
        Guid operatorId = UuidV7.NewGuid();
        Guid customerId = UuidV7.NewGuid();
        Guid bankAccountId = UuidV7.NewGuid();
        Guid invoiceAId = UuidV7.NewGuid();
        Guid invoiceBId = UuidV7.NewGuid();
        Guid invoiceCId = UuidV7.NewGuid();

        await using (VumaRetailDbContext seed = TestDbContextFactory.For(connectionString, clock, principal, tenant))
        {
            Tenant seeded = Tenant.CreateWithSouthAfricanDefaults("Group Harness (Pty) Ltd", "Harness");
            seeded.Activate();
            seed.Tenants.Add(seeded);

            Store store = Store.Create(seeded.Id, "JHB01", "Harness Sandton");
            seed.Stores.Add(store);
            await seed.CommitAsync().ConfigureAwait(false);

            tenantId = seeded.Id;
            storeId = store.Id;
        }

        await using (VumaRegistryDbContext registrySeed = TestDbContextFactory.ForRegistry(connectionString, tenant))
        {
            registrySeed.Companies.Add(ActiveCompany(tenantId, "GRA", "Group Co A", operatorId));
            registrySeed.Companies.Add(ActiveCompany(tenantId, "GRB", "Group Co B", operatorId));
            registrySeed.Companies.Add(ActiveCompany(tenantId, "GRC", "Group Co C", operatorId));
            await registrySeed.SaveChangesAsync().ConfigureAwait(false);

            // Company ids are minted inside Create; read them back so the finance seed below
            // books every row against the same companies the registry knows.
            List<Company> companies = await registrySeed.Companies
                .OrderBy(c => c.Code)
                .ToListAsync().ConfigureAwait(false);
            companyAId = companies.Single(c => c.Code == "GRA").Id;
            companyBId = companies.Single(c => c.Code == "GRB").Id;
            companyCId = companies.Single(c => c.Code == "GRC").Id;
        }

        await using (VumaRetailDbContext seed = TestDbContextFactory.For(connectionString, clock, principal, tenant))
        {
            SeedCompanyFinance(seed, "A", tenantId, storeId, clock,
                companyAId, "INV-A-0001", 1000m, invoiceAId, customerId);
            SeedCompanyFinance(seed, "B", tenantId, storeId, clock,
                companyBId, "INV-B-0001", 3000m, invoiceBId, customerId);
            SeedCompanyFinance(seed, "C", tenantId, storeId, clock,
                companyCId, "INV-C-0001", 5000m, invoiceCId, customerId);
            await seed.CommitAsync().ConfigureAwait(false);
        }

        tenant.SetTenant(tenantId, storeId);
        tenant.EndBypass();

        VumaRetailDbContext context = TestDbContextFactory.For(connectionString, clock, principal, tenant);
        VumaRegistryDbContext registry = TestDbContextFactory.ForRegistry(connectionString, tenant);

        return new GroupReceiptHarness(
            connectionString, context, registry, clock, tenant, principal, guard,
            tenantId, storeId, companyAId, companyBId, companyCId, operatorId,
            customerId, bankAccountId, invoiceAId, invoiceBId, invoiceCId);
    }

    /// <summary>Builds the real allocation saga over this harness's databases.</summary>
    public IGroupReceiptService CreateService(ICompanyLinkGuard? links = null)
    {
        ICompanyLinkGuard guard = links ?? Links;

        var replication = new ReplicationRegistry(Context);
        var scope = new ReplicationScope();
        var replicas = new ReplicaWriter(Context, scope);
        var node = new NodeIdentity(new NodeIdentityOptions { NodeId = "store:harness", Kind = NodeKind.Store });
        var hybrid = new HybridLogicalClock(Clock, node);

        var legs = new GroupReceiptLegDispatcher(
            _scopes.GetRequiredService<IServiceScopeFactory>(),
            replication,
            replicas,
            hybrid,
            node,
            Clock,
            NullLogger<GroupReceiptLegDispatcher>.Instance);

        return new GroupReceiptService(
            new GroupReceiptRepository(Registry),
            Registry,
            guard,
            legs,
            hybrid,
            Principal,
            Clock,
            Registry,
            NullLogger<GroupReceiptService>.Instance);
    }

    /// <summary>Takes one company's database "down": its legs fail until it is back.</summary>
    public void SetCompanyDown(Guid companyId, bool down)
    {
        lock (_downCompanies)
        {
            if (down)
            {
                _downCompanies.Add(companyId);
            }
            else
            {
                _downCompanies.Remove(companyId);
            }
        }
    }

    /// <summary>Opens a fresh company context over the test database.</summary>
    public VumaRetailDbContext OpenCompanyDb()
    {
        VumaRetailDbContext context = TestDbContextFactory.For(
            ConnectionString, Clock, Principal, TenantContext);
        Track(context);
        return context;
    }

    private void Track(VumaRetailDbContext context)
    {
        lock (_owned)
        {
            _owned.Add(context);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _scopes.DisposeAsync().ConfigureAwait(false);

        List<IAsyncDisposable> owned;
        lock (_owned)
        {
            owned = [.. _owned];
            _owned.Clear();
        }

        foreach (IAsyncDisposable disposable in owned)
        {
            if (!ReferenceEquals(disposable, _scopes))
            {
                await disposable.DisposeAsync().ConfigureAwait(false);
            }
        }

        await Context.DisposeAsync().ConfigureAwait(false);
        await Registry.DisposeAsync().ConfigureAwait(false);
    }

    private static Company ActiveCompany(Guid tenantId, string code, string name, Guid operatorId)
    {
        Company company = Company.Create(tenantId, code, name, name, "ZAR", "en-ZA", code);
        company.AssignOperator(operatorId);
        company.SetConnectionSecretRef($"test-secret-{code}");
        company.SetLifecycle(CompanyLifecycleState.Seeding);
        company.SetLifecycle(CompanyLifecycleState.Registered);
        company.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
        return company;
    }

    private static void SeedCompanyFinance(
        VumaRetailDbContext seed,
        string companyCode,
        Guid tenantId,
        Guid storeId,
        TestClock clock,
        Guid companyId,
        string invoiceNumber,
        decimal invoiceTotal,
        Guid invoiceId,
        Guid customerId)
    {
        Account bank = Account.Open(tenantId, $"{companyCode}000", $"Company {companyCode} bank", AccountType.Asset, "ZAR");
        Account debtors = Account.Open(tenantId, $"{companyCode}100", $"Company {companyCode} trade debtors", AccountType.Asset, "ZAR", ControlAccountType.AccountsReceivable);
        Account clearing = Account.Open(tenantId, $"{companyCode}500", $"Company {companyCode} inter-company clearing", AccountType.Asset, "ZAR");
        foreach (Account account in new[] { bank, debtors, clearing })
        {
            account.AssignCompany(companyId);
            seed.Accounts.Add(account);
        }

        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        AccountingPeriod period = AccountingPeriod.Open(
            tenantId, new DateOnly(today.Year, today.Month, 1), today.AddMonths(2));
        period.AssignCompany(companyId);
        seed.AccountingPeriods.Add(period);

        // Own share: cash into this company's bank, customer settled.
        PostingRule allocated = PostingRule.Define(tenantId, GroupReceiptLegDispatcher.AllocatedEventType, "Group receipt share");
        allocated.AssignCompany(companyId);
        allocated.AddLine(bank.Id, NormalBalance.Debit, "Gross");
        allocated.AddLine(debtors.Id, NormalBalance.Credit, "Gross");
        seed.PostingRules.Add(allocated);

        // Sister share: cash sits in the bank owner's bank, so this company books clearing.
        PostingRule allocatedSister = PostingRule.Define(tenantId, GroupReceiptLegDispatcher.AllocatedSisterEventType, "Group receipt sister share");
        allocatedSister.AssignCompany(companyId);
        allocatedSister.AddLine(clearing.Id, NormalBalance.Debit, "Gross");
        allocatedSister.AddLine(debtors.Id, NormalBalance.Credit, "Gross");
        seed.PostingRules.Add(allocatedSister);

        // Bank side: the bank owner books cash against clearing for each sister share.
        PostingRule clearingRaised = PostingRule.Define(tenantId, GroupReceiptLegDispatcher.ClearingRaisedEventType, "Inter-company clearing raised");
        clearingRaised.AssignCompany(companyId);
        clearingRaised.AddLine(bank.Id, NormalBalance.Debit, "Gross");
        clearingRaised.AddLine(clearing.Id, NormalBalance.Credit, "Gross");
        seed.PostingRules.Add(clearingRaised);

        PostingRule reversed = PostingRule.Define(tenantId, GroupReceiptLegDispatcher.ReversedEventType, "Group receipt reversal");
        reversed.AssignCompany(companyId);
        reversed.AddLine(debtors.Id, NormalBalance.Debit, "Gross");
        reversed.AddLine(bank.Id, NormalBalance.Credit, "Gross");
        seed.PostingRules.Add(reversed);

        PostingRule reversedSister = PostingRule.Define(tenantId, GroupReceiptLegDispatcher.ReversedSisterEventType, "Group receipt sister reversal");
        reversedSister.AssignCompany(companyId);
        reversedSister.AddLine(debtors.Id, NormalBalance.Debit, "Gross");
        reversedSister.AddLine(clearing.Id, NormalBalance.Credit, "Gross");
        seed.PostingRules.Add(reversedSister);

        PostingRule clearingReversed = PostingRule.Define(tenantId, GroupReceiptLegDispatcher.ClearingReversedEventType, "Inter-company clearing reversed");
        clearingReversed.AssignCompany(companyId);
        clearingReversed.AddLine(clearing.Id, NormalBalance.Debit, "Gross");
        clearingReversed.AddLine(bank.Id, NormalBalance.Credit, "Gross");
        seed.PostingRules.Add(clearingReversed);

        foreach (PostingRule rule in seed.PostingRules.Local.ToList())
        {
            foreach (PostingRuleLine line in rule.Lines)
            {
                line.AssignCompany(companyId);
            }
        }

        ArInvoice invoice = ArInvoice.Draft(
            tenantId, storeId, PartnerId.From(customerId), invoiceNumber,
            today.AddDays(-10), today.AddDays(20), "ZAR");
        invoice.AddLine("Goods", new Money(invoiceTotal, "ZAR"), "STANDARD", Money.Zero("ZAR"));
        invoice.Post(Guid.NewGuid());
        invoice.AssignCompany(companyId);
        SetEntityId(invoice, invoiceId);
        seed.ArInvoices.Add(invoice);
        foreach (ArInvoiceLine line in invoice.Lines)
        {
            line.AssignCompany(companyId);
            seed.ArInvoiceLines.Add(line);
        }
    }

    private static void SetEntityId(object entity, Guid id)
    {
        var property = entity.GetType().GetProperty("Id")
            ?? throw new InvalidOperationException($"Entity {entity.GetType().Name} has no Id.");
        property.SetValue(entity, id);
    }

    private sealed class OutageCompanyFactory(
        string connectionString,
        TestClock clock,
        TestPrincipalAccessor principal,
        ITenantContext tenant,
        ICompanyContext company,
        HashSet<Guid> downCompanies,
        Action<VumaRetailDbContext> track) : ICompanyDbContextFactory
    {
        public Task<VumaRetailDbContext> CreateAsync(CancellationToken cancellationToken = default)
            => CreateAsync(CompanyAccessMode.Write, cancellationToken);

        public Task<VumaRetailDbContext> CreateAsync(CompanyAccessMode access, CancellationToken cancellationToken = default)
        {
            Guid companyId = company.RequireCompany();
            lock (downCompanies)
            {
                if (downCompanies.Contains(companyId))
                {
                    throw new InvalidOperationException(
                        $"Company {companyId} is not available for business operations.");
                }
            }

            VumaRetailDbContext context = TestDbContextFactory.For(connectionString, clock, principal, tenant);
            track(context);
            return Task.FromResult(context);
        }
    }
}
