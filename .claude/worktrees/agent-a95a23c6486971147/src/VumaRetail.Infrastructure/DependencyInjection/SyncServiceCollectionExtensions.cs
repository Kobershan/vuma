using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Backup;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Infrastructure.Backup;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Sync;
using VumaRetail.Sync.Clock;
using VumaRetail.Sync.Dispatch;
using VumaRetail.Sync.Permissions;
using VumaRetail.Sync.Protocol;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers Stage 04's replication and backup with the host's container.</summary>
public static class SyncServiceCollectionExtensions
{
    /// <summary>
    /// Registers the outbox, the inbox, the hybrid clock, the receiver and the outbox behaviour.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="node">This node's id and tier.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Call after <c>AddVumaPersistence</c>. The outbox behaviour reads the <c>DbContext</c>'s change
    /// tracker, and the replication registry is built from its model.
    /// </para>
    /// <para>
    /// The behaviour goes into pipeline slot 300, which Stage 03 reserved and left empty. That slot
    /// is inside slot 200's transaction, so the outbox row and the change it describes commit
    /// together — the property ADR-006 is entirely about.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVumaSync(this IServiceCollection services, NodeIdentityOptions node)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(node);

        services.AddSingleton(node);
        services.AddSingleton<INodeIdentity, NodeIdentity>();

        // Singleton, and it has to be. A hybrid logical clock's whole job is to be monotonic across
        // everything one node issues; a scoped one would restart at the wall clock on every request
        // and re-issue stamps it had already used within the same millisecond.
        services.AddSingleton<IHybridClock, HybridLogicalClock>();

        // Scoped, and it has to be. It records "this scope is applying a remote batch", and a
        // singleton would apply that to every till in the store for as long as one sync ran.
        services.AddScoped<IReplicationScope, ReplicationScope>();

        services.AddScoped<IReplicationRegistry>(provider =>
            new ReplicationRegistry(provider.GetRequiredService<VumaRetailDbContext>()));

        services.AddScoped<IReplicaWriter, ReplicaWriter>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IInboxRepository, InboxRepository>();
        services.AddScoped<ISyncCursorRepository, SyncCursorRepository>();
        services.AddScoped<IConflictRepository, ConflictRepository>();
        services.AddScoped<SyncReceiver>();

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IPipelineBehaviour, OutboxBehaviour>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, SyncPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, BackupPermissions>());

        // The sync handlers and validators live in VumaRetail.Sync, which the Application-only scan
        // does not reach.
        services.AddVumaMessaging(typeof(VumaRetail.Sync.AssemblyMarker).Assembly);

        return services;
    }

    /// <summary>
    /// Registers the outbox dispatcher and the transport that ships to a peer.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="peer">Where the peer is.</param>
    /// <param name="dispatcher">How the dispatcher is paced.</param>
    /// <param name="hostTenant">The tenant a background pass runs as.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// Separate from <see cref="AddVumaSync"/> because the two are needed in different places. The
    /// cloud tier <em>receives</em> and does not dispatch; a store server with no cloud subscription
    /// still captures its outbox, so its rows are there the day it signs up.
    /// </remarks>
    public static IServiceCollection AddVumaOutboxDispatcher(
        this IServiceCollection services,
        SyncPeerOptions peer,
        OutboxDispatcherOptions dispatcher,
        HostTenantResolver hostTenant)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(hostTenant);

        services.AddSingleton(peer);
        services.AddSingleton(dispatcher);
        services.AddScoped(_ => hostTenant);

        services.AddHttpClient<ISyncTransport, HttpSyncTransport>(client =>
        {
            if (!string.IsNullOrWhiteSpace(peer.BaseAddress))
            {
                client.BaseAddress = new Uri(peer.BaseAddress);
            }

            client.Timeout = peer.Timeout;
        });

        services.AddScoped<OutboxDispatcher>();
        services.AddHostedService<OutboxDispatcherService>();

        return services;
    }

    /// <summary>
    /// Registers the backup engine, cipher, vault and service (R4).
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="connectionString">The database to snapshot.</param>
    /// <param name="vault">Which vault and how to reach it.</param>
    /// <param name="encryption">The snapshot encryption key.</param>
    /// <param name="postgres">Where the PostgreSQL client tools are.</param>
    /// <returns>The container, for chaining.</returns>
    public static IServiceCollection AddVumaBackup(
        this IServiceCollection services,
        string connectionString,
        BackupVaultOptions vault,
        SnapshotEncryptionOptions encryption,
        PostgresBackupOptions? postgres = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(encryption);

        PostgresBackupOptions tools = postgres ?? new PostgresBackupOptions();

        services.AddSingleton(vault);
        services.AddSingleton(encryption);
        services.AddSingleton(tools);

        services.AddSingleton<ISnapshotCipher, AesGcmSnapshotCipher>();

        services.AddScoped<IBackupEngine>(provider => new PostgresBackupEngine(
            connectionString,
            tools,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PostgresBackupEngine>>()));

        services.AddScoped<IBackupSnapshotRepository, BackupSnapshotRepository>();
        services.AddScoped<IBackupService, BackupService>();

        AddVault(services, vault);

        return services;
    }

    private static void AddVault(IServiceCollection services, BackupVaultOptions options)
    {
        if (!string.Equals(options.Provider, "S3", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IBackupVault, FileSystemBackupVault>();
            return;
        }

        services.AddSingleton<IAmazonS3>(_ =>
        {
            AmazonS3Config config = new()
            {
                ForcePathStyle = options.ForcePathStyle,
            };

            if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
            {
                // Any S3-compatible endpoint: Backblaze B2, Wasabi, MinIO (CLAUDE.md §4).
                config.ServiceURL = options.ServiceUrl;
            }
            else if (!string.IsNullOrWhiteSpace(options.Region))
            {
                config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region);
            }

            return string.IsNullOrWhiteSpace(options.AccessKeyId)
                // No explicit key means the ambient credential chain — an instance role, or the
                // environment. That is the right default in a container and the wrong thing to
                // require of a shop's back-office PC, which is why both are supported.
                ? new AmazonS3Client(config)
                : new AmazonS3Client(
                    new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
                    config);
        });

        services.AddSingleton<IBackupVault, S3BackupVault>();
    }
}
