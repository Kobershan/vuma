using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Sync;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Persistence.Interceptors;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Sync;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Sync.Clock;
using VumaRetail.Sync.Protocol;

namespace VumaRetail.IntegrationTests.Sync;

/// <summary>
/// One node — tenant, store, database, outbox and receiver — wired the way a host wires it.
/// </summary>
/// <remarks>
/// <para>
/// Two of these make a replication pair, and that is the point: the store-to-cloud protocol is
/// exercised against two real databases with two real hybrid clocks, not against a mock of a peer.
/// The transport between them is <c>InProcessSyncTransport</c>, which is a production class rather
/// than a test double — it is what a single-till shop uses when its terminal and store server are the
/// same machine.
/// </para>
/// <para>
/// The commands run through the real dispatcher, so the outbox behaviour in slot 300 is exercised
/// inside the transaction behaviour in slot 200. A harness that called the receiver directly would
/// prove the receiver and nothing about ADR-006's atomicity, which is the whole claim.
/// </para>
/// </remarks>
public sealed class SyncHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private SyncHarness(
        VumaRetailDbContext context,
        TestTenantContext tenant,
        TestClock clock,
        NodeIdentityOptions node,
        Guid tenantId,
        Guid storeId,
        string connectionString,
        IPrincipalAccessor principal,
        AuditStamper stamper,
        IReplicationScope replicationScope)
    {
        Context = context;
        TenantContext = tenant;
        Clock = clock;
        Node = node;
        TenantId = tenantId;
        StoreId = storeId;
        ConnectionString = connectionString;

        ServiceCollection services = new();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(context);
        services.AddSingleton<IUnitOfWork>(context);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IPrincipalAccessor>(principal);
        services.AddSingleton(stamper);

        // Identity is the only module with entities to replicate until Stage 06 builds master data,
        // so its repositories are what these tests change rows through. Registered rather than
        // faked: an outbox test that captured a change made by a stub would assert the stub.
        services.AddSingleton<VumaRetail.Application.Identity.IUserRepository>(new UserRepository(context));
        services.AddSingleton<VumaRetail.Application.Identity.IRoleRepository>(new RoleRepository(context));
        services.AddSingleton<VumaRetail.Application.Identity.ITerminalRepository>(
            new TerminalRepository(context, tenant));
        services.AddSingleton<VumaRetail.Application.Identity.IPasswordHasher>(
            new VumaRetail.Infrastructure.Security.Identity.IdentityPasswordHasher());
        services.AddSingleton<VumaRetail.Application.Identity.ITokenHasher>(
            new VumaRetail.Infrastructure.Security.Identity.Sha256TokenHasher());
        services.AddSingleton(VumaRetail.Domain.Identity.CredentialPolicy.Default);
        services.AddSingleton<VumaRetail.Application.Identity.Permissions.IPermissionCatalogue>(
            new VumaRetail.Application.Identity.Permissions.PermissionCatalogue(
            [
                new VumaRetail.Application.Identity.Permissions.PlatformPermissions(),
                new VumaRetail.Application.Identity.Permissions.IdentityPermissions(),
                new VumaRetail.Sync.Permissions.SyncPermissions(),
                new VumaRetail.Sync.Permissions.BackupPermissions(),
            ]));

        services.AddSingleton(node);
        services.AddSingleton<INodeIdentity, NodeIdentity>();
        services.AddSingleton<IHybridClock, HybridLogicalClock>();
        // The very same instance the audit stamper reads. Two instances would mean the stamper
        // could not tell a locally-made change from one applied on a peer's instruction.
        services.AddSingleton(replicationScope);
        services.AddSingleton<IReplicationRegistry>(_ => new ReplicationRegistry(context));
        services.AddSingleton<IReplicaWriter, ReplicaWriter>();
        services.AddSingleton<IOutboxRepository, OutboxRepository>();
        services.AddSingleton<IInboxRepository, InboxRepository>();
        services.AddSingleton<ISyncCursorRepository, SyncCursorRepository>();
        services.AddSingleton<IConflictRepository, ConflictRepository>();
        services.AddSingleton<SyncReceiver>();
        services.AddSingleton<IPipelineBehaviour, OutboxBehaviour>();

        services.AddVumaMessaging(
            typeof(VumaRetail.Application.AssemblyMarker).Assembly,
            typeof(VumaRetail.Sync.AssemblyMarker).Assembly);

        _services = services.BuildServiceProvider();
        Dispatcher = _services.GetRequiredService<IDispatcher>();
    }

    /// <summary>The Stage 03 dispatcher, with the outbox behaviour in slot 300.</summary>
    public IDispatcher Dispatcher { get; }

    /// <summary>The database context under test.</summary>
    public VumaRetailDbContext Context { get; }

    /// <summary>The tenant context, so a test can move between tenants.</summary>
    public TestTenantContext TenantContext { get; }

    /// <summary>The clock the test moves by hand.</summary>
    public TestClock Clock { get; }

    /// <summary>This node's id and tier.</summary>
    public NodeIdentityOptions Node { get; }

    /// <summary>The seeded tenant. Shared by both nodes of a pair — it is one tenant's data.</summary>
    public Guid TenantId { get; }

    /// <summary>The seeded store.</summary>
    public Guid StoreId { get; }

    /// <summary>This node's database, for the backup tests.</summary>
    public string ConnectionString { get; }

    /// <summary>This node's hybrid logical clock.</summary>
    public IHybridClock HybridClock => _services.GetRequiredService<IHybridClock>();

    /// <summary>The receiver, for the tests that hand it a batch directly.</summary>
    public SyncReceiver Receiver => _services.GetRequiredService<SyncReceiver>();

    /// <summary>A transport pointed at this node, for use by its peer.</summary>
    public ISyncTransport AsPeerOf(string peerNodeId)
        => new InProcessSyncTransport(Node.NodeId, async (batch, cancellationToken) =>
        {
            _ = peerNodeId;

            SyncAcknowledgement acknowledgement = await Dispatcher
                .SendAsync(new VumaRetail.Sync.Commands.ReceiveSyncBatchCommand(batch), cancellationToken);

            return acknowledgement;
        });

    /// <summary>Creates a node over a fresh database with a tenant and a store already in it.</summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    /// <param name="nodeId">This node's id.</param>
    /// <param name="kind">This node's tier.</param>
    /// <param name="tenantId">The tenant, so a pair of nodes can share one.</param>
    /// <param name="storeId">The store, so a pair of nodes can share one.</param>
    public static async Task<SyncHarness> CreateAsync(
        PostgresFixture fixture,
        string nodeId = "store:test",
        NodeKind kind = NodeKind.Store,
        Guid? tenantId = null,
        Guid? storeId = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        string connectionString = await fixture.CreateDatabaseAsync();

        TestClock clock = new();
        TestTenantContext tenant = TestTenantContext.Unfiltered();

        // The principal is distinct per node, deliberately. With both nodes acting as the same
        // principal, an assertion that a replicated row keeps its *origin* passes trivially — the
        // two values match because nothing distinguished them, not because the origin survived.
        // Making them differ is what turned that assertion from a tautology into a test, and is how
        // the outbox-serialises-before-stamping bug was found.
        TestPrincipalAccessor principal = new($"user:{nodeId}");

        // One stamper, shared by the interceptor and the outbox behaviour. Stamping is idempotent
        // per save only because both see the same set of already-stamped entities.
        ReplicationScope replicationScope = new();
        AuditStamper stamper = new(clock, principal, replicationScope);

        VumaRetailDbContext context = TestDbContextFactory.For(
            connectionString,
            clock,
            principal,
            tenant,
            stamper);

        Guid seededTenant;
        Guid seededStore;

        if (tenantId is { } sharedTenant && storeId is { } sharedStore)
        {
            // The peer's database, holding the same tenant and store rows. Identity is by id, which
            // is what makes UUID v7 primary keys generated client-side (ADR-004) work across tiers
            // without a mapping table.
            Tenant peerTenant = Tenant.CreateWithSouthAfricanDefaults("Sync Retail (Pty) Ltd", "Sync");
            ForceId(peerTenant, sharedTenant);
            peerTenant.Activate();
            context.Tenants.Add(peerTenant);

            Store peerStore = Store.Create(sharedTenant, "JHB01", "Sync Sandton");
            ForceId(peerStore, sharedStore);
            context.Stores.Add(peerStore);

            await context.CommitAsync();

            seededTenant = sharedTenant;
            seededStore = sharedStore;
        }
        else
        {
            Tenant seeded = Tenant.CreateWithSouthAfricanDefaults("Sync Retail (Pty) Ltd", "Sync");
            seeded.Activate();
            context.Tenants.Add(seeded);

            Store store = Store.Create(seeded.Id, "JHB01", "Sync Sandton");
            context.Stores.Add(store);

            await context.CommitAsync();

            seededTenant = seeded.Id;
            seededStore = store.Id;
        }

        // Arranged unfiltered, exercised filtered — the receiver re-scopes rather than bypassing,
        // and a test that left the bypass open would prove nothing about that.
        tenant.SetTenant(seededTenant, seededStore);
        tenant.EndBypass();

        return new SyncHarness(
            context,
            tenant,
            clock,
            new NodeIdentityOptions { NodeId = nodeId, Kind = kind },
            seededTenant,
            seededStore,
            connectionString,
            principal,
            stamper,
            replicationScope);
    }

    /// <summary>
    /// Forces a client-generated id onto a seeded row, so two nodes hold the same tenant.
    /// </summary>
    /// <remarks>
    /// Reflection, and only in the harness. <c>Entity.Id</c> is <c>protected set</c> precisely so
    /// business code cannot do this; what a test needs here is the state a real second tier reaches
    /// by having replicated the row, and there is no replication of tenants until Stage 06 creates
    /// them through a command.
    /// </remarks>
    private static void ForceId(VumaRetail.Domain.Entities.Entity entity, Guid id)
        => typeof(VumaRetail.Domain.Entities.Entity)
            .GetProperty(nameof(VumaRetail.Domain.Entities.Entity.Id))!
            .SetValue(entity, id);

    /// <summary>Sends a command through the real pipeline, outbox behaviour included.</summary>
    /// <typeparam name="TResult">What the command returns.</typeparam>
    /// <param name="command">The command.</param>
    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command) => Dispatcher.SendAsync(command);

    /// <summary>Sends a query through the real pipeline.</summary>
    /// <typeparam name="TResult">What the query returns.</typeparam>
    /// <param name="query">The query.</param>
    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query) => Dispatcher.QueryAsync(query);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await Context.DisposeAsync();
    }
}
