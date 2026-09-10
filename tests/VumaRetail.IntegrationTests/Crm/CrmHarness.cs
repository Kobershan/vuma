using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Crm;
using VumaRetail.Application.Partners;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Partners;
using VumaRetail.Domain.Platform;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Crm;

/// <summary>
/// A tenant that can already trade, plus the <c>crm</c> module wired over a real database
/// through the Stage 03 dispatcher (Stage 19).
/// </summary>
public sealed class CrmHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private CrmHarness(
        VumaRetailDbContext context,
        TestTenantContext tenant,
        TestClock clock,
        TestPrincipalAccessor principal,
        Guid tenantId,
        Guid storeId,
        Guid companyId,
        Guid customerId)
    {
        Context = context;
        TenantContext = tenant;
        Clock = clock;
        Principal = principal;
        TenantId = tenantId;
        StoreId = storeId;
        CompanyId = companyId;
        CustomerId = customerId;

        Leads = new LeadRepository(context);
        Opportunities = new OpportunityRepository(context);
        Activities = new ActivityRepository(context);
        Segments = new SegmentRepository(context);
        Members = new SegmentMemberRepository(context);
        Consents = new ConsentRepository(context);
        Partners = new PartnerRepository(context);

        ServiceCollection services = new();

        services.AddLogging();
        services.AddSingleton<IUnitOfWork>(context);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IPrincipalAccessor>(principal);

        services.AddSingleton<ILeadRepository>(Leads);
        services.AddSingleton<IOpportunityRepository>(Opportunities);
        services.AddSingleton<IActivityRepository>(Activities);
        services.AddSingleton<ISegmentRepository>(Segments);
        services.AddSingleton<ISegmentMemberRepository>(Members);
        services.AddSingleton<IConsentRepository>(Consents);
        services.AddSingleton<IPartnerRepository>(Partners);

        services.AddSingleton<IConsentService, ConsentService>();
        services.AddSingleton<ISegmentService, SegmentService>();
        services.AddSingleton<ICustomer360ViewService, Customer360ViewService>();

        services.AddVumaMessaging();

        _services = services.BuildServiceProvider();
        Dispatcher = _services.GetRequiredService<IDispatcher>();
    }

    /// <summary>The Stage 03 dispatcher, with validation, transaction and logging in the chain.</summary>
    public IDispatcher Dispatcher { get; }

    /// <summary>The database context under test.</summary>
    public VumaRetailDbContext Context { get; }

    /// <summary>The tenant context.</summary>
    public TestTenantContext TenantContext { get; }

    /// <summary>The clock the test moves by hand.</summary>
    public TestClock Clock { get; }

    /// <summary>The principal.</summary>
    public TestPrincipalAccessor Principal { get; }

    /// <summary>The seeded tenant.</summary>
    public Guid TenantId { get; }

    /// <summary>The seeded store.</summary>
    public Guid StoreId { get; }

    /// <summary>The acting company (column value; no company scope is bound).</summary>
    public Guid CompanyId { get; }

    /// <summary>A partner marked as a customer — the conversion link target.</summary>
    public Guid CustomerId { get; }

    /// <summary>Lead repository — the proof rows really landed.</summary>
    public ILeadRepository Leads { get; }

    /// <summary>Opportunity repository.</summary>
    public IOpportunityRepository Opportunities { get; }

    /// <summary>Activity repository.</summary>
    public IActivityRepository Activities { get; }

    /// <summary>Segment repository.</summary>
    public ISegmentRepository Segments { get; }

    /// <summary>Membership repository.</summary>
    public ISegmentMemberRepository Members { get; }

    /// <summary>Consent repository.</summary>
    public IConsentRepository Consents { get; }

    /// <summary>Partner repository.</summary>
    public IPartnerRepository Partners { get; }

    /// <summary>Creates a harness over a fresh database with a tenant, a store and a customer.</summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public static async Task<CrmHarness> CreateAsync(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        string connectionString = await fixture.CreateDatabaseAsync().ConfigureAwait(false);

        TestClock clock = new();
        TestTenantContext tenant = TestTenantContext.Unfiltered();

        Domain.Platform.Tenant seeded = Domain.Platform.Tenant.CreateWithSouthAfricanDefaults(
            "CRM Harness (Pty) Ltd", "Harness");
        seeded.Activate();

        Store store = Store.Create(seeded.Id, "JHB01", "Harness Sandton");
        User clerk = User.Create(seeded.Id, "clerk", "Crm Clerk");

        TestPrincipalAccessor principal = new($"user:{clerk.Id}", terminalId: null);

        VumaRetailDbContext context = TestDbContextFactory.For(connectionString, clock, principal, tenant);

        context.Tenants.Add(seeded);
        context.Stores.Add(store);
        context.Users.Add(clerk);

        Partner customer = Partner.Create(seeded.Id, "CUS-001", "Harness Customer", PartnerType.Customer);
        context.Partners.Add(customer);

        await context.SaveChangesAsync().ConfigureAwait(false);

        Guid companyId = Guid.NewGuid();
        tenant.SetTenant(seeded.Id, store.Id);

        return new CrmHarness(
            context, tenant, clock, principal, seeded.Id, store.Id, companyId, customer.Id);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync().ConfigureAwait(false);
        await Context.DisposeAsync().ConfigureAwait(false);
    }
}
