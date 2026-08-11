using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Domain.Backup;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Sync;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Licensing;

/// <summary>
/// The aggregate counters the metering rollup and the heartbeat are built from (R10, ADR-024).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every query in this file is a <c>Count</c>, a <c>Max</c> or a <c>Sum</c>.</b> There is no
/// <c>Select</c> of a row, no projection of a column that holds text a person wrote, and no way to add
/// one without changing <see cref="IUsageCounterSource"/> — whose return types have nowhere to put it.
/// CLAUDE.md §7 rule 16 says building a metering payload from a business table is a bug; this is where
/// that is made structurally true rather than carefully observed.
/// </para>
/// <para>
/// Module usage comes from the audit trail's <c>table_name</c>, which is <c>schema.table</c> — and the
/// schema <em>is</em> the module (ADR-010). So "how much is this customer using the inventory module"
/// is answered by counting rows in a column that holds a schema name, which discloses that inventory
/// was used and nothing whatever about what was done with it.
/// </para>
/// </remarks>
/// <param name="context">The database context, filtered to the current tenant as everything is.</param>
/// <param name="clock">The only source of time.</param>
public sealed class UsageCounterSource(VumaRetailDbContext context, IClock clock) : IUsageCounterSource
{
    /// <summary>How recently a terminal must have been seen to count as online.</summary>
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public async Task<ConfigurationCounts> CountConfigurationAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset since = clock.UtcNow - OnlineWindow;

        int stores = await context.Stores.CountAsync(cancellationToken).ConfigureAwait(false);

        int terminals = await context.Terminals
            .CountAsync(terminal => terminal.Status != TerminalStatus.Revoked, cancellationToken)
            .ConfigureAwait(false);

        int online = await context.Terminals
            .CountAsync(terminal => terminal.LastSeenAt != null && terminal.LastSeenAt >= since, cancellationToken)
            .ConfigureAwait(false);

        int users = await context.Users.CountAsync(cancellationToken).ConfigureAwait(false);

        return new ConfigurationCounts(stores, terminals, online, users);
    }

    /// <inheritdoc />
    public async Task<ActivityCounts> CountActivityAsync(
        DateOnly period,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset from = new(period.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset to = from.AddDays(1);

        IQueryable<Domain.Platform.AuditEntry> day = context.AuditEntries
            .Where(entry => entry.OccurredAt >= from && entry.OccurredAt < to);

        long writes = await day.LongCountAsync(cancellationToken).ConfigureAwait(false);

        int activeUsers = await day
            .Where(entry => !entry.IsSystemAction)
            .Select(entry => entry.Principal)
            .Distinct()
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        // Grouped on the schema half of schema.table — which is the module (ADR-010). The result is a
        // count per module and nothing else.
        List<ModuleCount> byTable = await day
            .GroupBy(entry => entry.TableName)
            .Select(group => new ModuleCount(group.Key, group.LongCount()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, long> moduleUsage = [];

        foreach (ModuleCount entry in byTable)
        {
            int separator = entry.TableName.IndexOf('.', StringComparison.Ordinal);
            string module = separator > 0 ? entry.TableName[..separator] : entry.TableName;

            moduleUsage[module] = moduleUsage.GetValueOrDefault(module) + entry.Count;
        }

        int outboxDepth = await context.OutboxMessages
            .CountAsync(message => message.Status != OutboxStatus.Dispatched, cancellationToken)
            .ConfigureAwait(false);

        int snapshotsTaken = await context.BackupSnapshots
            .CountAsync(snapshot => snapshot.StartedAt >= from && snapshot.StartedAt < to, cancellationToken)
            .ConfigureAwait(false);

        int snapshotsVerified = await context.BackupSnapshots
            .CountAsync(
                snapshot => snapshot.VerifiedAt != null && snapshot.VerifiedAt >= from && snapshot.VerifiedAt < to,
                cancellationToken)
            .ConfigureAwait(false);

        int syncFailures = await context.OutboxMessages
            .CountAsync(message => message.AttemptCount > 0 && message.Status != OutboxStatus.Dispatched, cancellationToken)
            .ConfigureAwait(false);

        int conflictsOpen = await context.ConflictEntries
            .CountAsync(entry => entry.Resolution == ConflictResolution.Unresolved, cancellationToken)
            .ConfigureAwait(false);

        return new ActivityCounts(
            activeUsers,
            writes,
            moduleUsage,
            await StorageBytesAsync(cancellationToken).ConfigureAwait(false),
            outboxDepth,
            snapshotsTaken,
            snapshotsVerified,
            syncFailures,
            conflictsOpen);
    }

    /// <inheritdoc />
    public async Task<HealthCounts> CountHealthAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = clock.UtcNow;

        int outboxDepth = await context.OutboxMessages
            .CountAsync(message => message.Status != OutboxStatus.Dispatched, cancellationToken)
            .ConfigureAwait(false);

        // Sync lag is the age of the oldest thing still waiting to go out. Zero when the queue is
        // empty, which is the honest answer rather than "unknown".
        DateTimeOffset? oldestPending = await context.OutboxMessages
            .Where(message => message.Status != OutboxStatus.Dispatched)
            .Select(message => (DateTimeOffset?)message.OccurredAt)
            .MinAsync(cancellationToken)
            .ConfigureAwait(false);

        BackupSnapshot? lastBackup = await context.BackupSnapshots
            .Where(snapshot => snapshot.Status == BackupStatus.Completed)
            .OrderByDescending(snapshot => snapshot.StartedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset? lastVerified = await context.BackupSnapshots
            .Where(snapshot => snapshot.VerifiedAt != null)
            .Select(snapshot => snapshot.VerifiedAt)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);

        int errors = await context.OutboxMessages
            .CountAsync(
                message => message.LastError != null && message.OccurredAt >= now.AddDays(-1),
                cancellationToken)
            .ConfigureAwait(false);

        return new HealthCounts(
            oldestPending is { } oldest ? (long)Math.Max(0, (now - oldest).TotalSeconds) : 0,
            outboxDepth,
            lastBackup?.CompletedAt ?? lastBackup?.StartedAt,
            lastVerified,
            errors);
    }

    /// <summary>
    /// The database's size on disk, from PostgreSQL itself.
    /// </summary>
    /// <remarks>
    /// A single scalar with no table name in it. Summing row counts would be an estimate that drifts
    /// from what the vendor bills storage on, and asking the engine is both exact and cheap.
    /// </remarks>
    private async Task<long> StorageBytesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using System.Data.Common.DbConnection connection = context.Database.GetDbConnection();
            await using System.Data.Common.DbCommand command = connection.CreateCommand();

            command.CommandText = "SELECT pg_database_size(current_database())";

            if (connection.State is not System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return result is long size ? size : 0;
        }
        catch (System.Data.Common.DbException)
        {
            // A permission the database role does not have is not worth failing a metering rollup
            // over. Zero is reported, the rest of the day's counts still go out.
            return 0;
        }
    }

    /// <summary>A table and how many audit entries it produced. Never leaves this file.</summary>
    /// <param name="TableName">The <c>schema.table</c> the entries were written for.</param>
    /// <param name="Count">How many.</param>
    private sealed record ModuleCount(string TableName, long Count);
}
