using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Backup;
using VumaRetail.Domain.Backup;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.StoreServer;

/// <summary>
/// The command-line face of requirement R4: take a snapshot, verify one, restore one.
/// </summary>
/// <remarks>
/// <para>
/// A restore has to be runnable when the application is not. "Store burns down → new box, run
/// restore, back trading" describes a machine with a fresh PostgreSQL and no data in it — there is no
/// back office to click in, and no API to call, because both are what is being restored.
/// </para>
/// <para>
/// It goes through <see cref="IBackupService"/> rather than shelling out to <c>pg_restore</c>
/// directly, so the checksum is confirmed and the ledger is stamped on the same path the API uses. A
/// restore script that bypassed the service would be a second implementation of the one operation
/// nobody gets to practise.
/// </para>
/// </remarks>
public static class BackupCli
{
    /// <summary>Takes a snapshot now and prints its id.</summary>
    /// <param name="services">The host's container.</param>
    /// <param name="tenantId">The tenant to back up.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static async Task<int> BackupAsync(
        IServiceProvider services,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using IServiceScope scope = services.CreateScope();
        Scope(scope, tenantId);

        BackupSnapshot snapshot = await scope.ServiceProvider
            .GetRequiredService<IBackupService>()
            .CreateSnapshotAsync(BackupKind.Full, cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"snapshot {snapshot.Id} — {snapshot.SizeBytes} bytes, sha256 {snapshot.Checksum}"));
        Console.WriteLine(snapshot.ObjectKey);

        return 0;
    }

    /// <summary>Reads a snapshot back from the vault and confirms its checksum.</summary>
    /// <param name="services">The host's container.</param>
    /// <param name="tenantId">The tenant the snapshot belongs to.</param>
    /// <param name="snapshotId">The snapshot, or <see cref="Guid.Empty"/> for the latest completed one.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static async Task<int> VerifyAsync(
        IServiceProvider services,
        Guid tenantId,
        Guid snapshotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using IServiceScope scope = services.CreateScope();
        Scope(scope, tenantId);

        Guid target = await ResolveAsync(scope, snapshotId, cancellationToken).ConfigureAwait(false);

        BackupSnapshot verified = await scope.ServiceProvider
            .GetRequiredService<IBackupService>()
            .VerifySnapshotAsync(target, cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"snapshot {verified.Id} verified at {verified.VerifiedAt:O}");

        return 0;
    }

    /// <summary>Restores a snapshot into a target database.</summary>
    /// <param name="services">The host's container.</param>
    /// <param name="tenantId">The tenant the snapshot belongs to.</param>
    /// <param name="snapshotId">The snapshot, or <see cref="Guid.Empty"/> for the latest completed one.</param>
    /// <param name="targetConnectionString">The database to restore into.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// The target is a required argument with no default, deliberately. A restore replaces a
    /// database, and a command that defaulted to the configured connection string would make
    /// "practise the restore" and "destroy the live store" one typo apart.
    /// </remarks>
    public static async Task<int> RestoreAsync(
        IServiceProvider services,
        Guid tenantId,
        Guid snapshotId,
        string targetConnectionString,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(targetConnectionString))
        {
            Console.Error.WriteLine(
                "--restore needs a target connection string. A restore replaces a database, so there "
                + "is no default.");

            return 2;
        }

        using IServiceScope scope = services.CreateScope();
        Scope(scope, tenantId);

        Guid target = await ResolveAsync(scope, snapshotId, cancellationToken).ConfigureAwait(false);

        BackupSnapshot restored = await scope.ServiceProvider
            .GetRequiredService<IBackupService>()
            .RestoreSnapshotAsync(target, targetConnectionString, cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"snapshot {restored.Id} restored at {restored.RestoredAt:O}");

        return 0;
    }

    private static void Scope(IServiceScope scope, Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Vuma:Host:TenantId is not configured, so there is no tenant to back up. A store "
                + "server serves exactly one tenant (R2).");
        }

        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
    }

    private static async Task<Guid> ResolveAsync(
        IServiceScope scope,
        Guid snapshotId,
        CancellationToken cancellationToken)
    {
        if (snapshotId != Guid.Empty)
        {
            return snapshotId;
        }

        BackupSnapshot latest = await scope.ServiceProvider
            .GetRequiredService<IBackupSnapshotRepository>()
            .FindLatestCompletedAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("There is no completed snapshot to work from.");

        return latest.Id;
    }

    /// <summary>Reads the value following a flag, or <c>null</c>.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="flag">The flag to look for.</param>
    public static string? ValueAfter(string[] args, string flag)
    {
        ArgumentNullException.ThrowIfNull(args);

        int index = Array.IndexOf(args, flag);

        return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[index + 1]
            : null;
    }

    /// <summary>Applies pending migrations. Shared by the host's <c>--migrate</c> switch.</summary>
    /// <param name="services">The host's container.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using IServiceScope scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<VumaRetailDbContext>()
            .Database
            .MigrateAsync(cancellationToken)
            .ConfigureAwait(false);

        // Both databases, not just the company's. A deployment migrated without the registry
        // schema serves every registry read — sign-in enrichment, operator resolution, company
        // links — as "relation does not exist" 500s.
        await scope.ServiceProvider
            .GetRequiredService<VumaRegistryDbContext>()
            .Database
            .MigrateAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
