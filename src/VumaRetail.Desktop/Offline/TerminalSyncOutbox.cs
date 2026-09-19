using System.IO;
using Microsoft.Data.Sqlite;
using VumaRetail.Contracts.Sync;

namespace VumaRetail.Desktop.Offline;

/// <summary>Durable terminal-side queue for operations captured before a store server is reachable.</summary>
/// <remarks>
/// The queue is deliberately a small SQLite store rather than an in-memory retry list. A terminal may
/// restart between a sale and the next network attempt, and the operation id must survive that restart
/// so the receiver's at-least-once dedupe remains effective. The caller supplies the already-formed
/// replicated operation; this class never invents a business payload or bypasses a command handler.
/// </remarks>
public sealed class TerminalSyncOutbox : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Task _initialization;

    /// <summary>Opens the terminal database at the supplied path.</summary>
    public TerminalSyncOutbox(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        _initialization = InitializeSchemaAsync();
    }

    /// <summary>Creates the outbox schema if this is the first terminal launch.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => _initialization.WaitAsync(cancellationToken);

    private async Task InitializeSchemaAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _connection.OpenAsync().ConfigureAwait(false);
            await using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS terminal_outbox (
                    operation_id TEXT PRIMARY KEY,
                    sale_id TEXT NOT NULL,
                    sequence INTEGER NOT NULL,
                    entity_type TEXT NOT NULL,
                    entity_id TEXT NOT NULL,
                    operation TEXT NOT NULL,
                    stamp TEXT NOT NULL,
                    payload TEXT NOT NULL,
                    occurred_at TEXT NOT NULL,
                    state TEXT NOT NULL DEFAULT 'Pending',
                    error TEXT NULL,
                    UNIQUE (sale_id, sequence)
                );
                CREATE INDEX IF NOT EXISTS ix_terminal_outbox_pending
                    ON terminal_outbox (state, sale_id, sequence);
                """;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Queues an operation exactly once, retaining sale order for replay.</summary>
    public async Task EnqueueAsync(Guid saleId, int sequence, SyncOperationDto operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO terminal_outbox
                (operation_id, sale_id, sequence, entity_type, entity_id, operation, stamp, payload, occurred_at)
                VALUES ($operation_id, $sale_id, $sequence, $entity_type, $entity_id, $operation, $stamp, $payload, $occurred_at);
                """;
            command.Parameters.AddWithValue("$operation_id", operation.OperationId.ToString("D"));
            command.Parameters.AddWithValue("$sale_id", saleId.ToString("D"));
            command.Parameters.AddWithValue("$sequence", sequence);
            command.Parameters.AddWithValue("$entity_type", operation.EntityType);
            command.Parameters.AddWithValue("$entity_id", operation.EntityId.ToString("D"));
            command.Parameters.AddWithValue("$operation", operation.Operation);
            command.Parameters.AddWithValue("$stamp", operation.Stamp);
            command.Parameters.AddWithValue("$payload", operation.Payload);
            command.Parameters.AddWithValue("$occurred_at", operation.OccurredAt.UtcDateTime.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Returns the next in-order pending operations, across sales.</summary>
    public async Task<IReadOnlyList<QueuedOperation>> PendingAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await _initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                SELECT operation_id, sale_id, sequence, entity_type, entity_id, operation, stamp, payload, occurred_at
                FROM terminal_outbox WHERE state = 'Pending' ORDER BY sale_id, sequence LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
            List<QueuedOperation> rows = [];
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new QueuedOperation(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetInt32(2),
                    new SyncOperationDto(
                        Guid.Parse(reader.GetString(0)),
                        reader.GetString(3),
                        Guid.Parse(reader.GetString(4)),
                        reader.GetString(5),
                        reader.GetString(6),
                        reader.GetString(7),
                        DateTimeOffset.Parse(reader.GetString(8)))));
            }
            return rows;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Marks an acknowledged operation applied/duplicate, or retains a rejected row for review.</summary>
    public async Task SettleAsync(SyncBatchResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        await _initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            foreach (SyncOperationOutcomeDto result in response.Results)
            {
                await using SqliteCommand command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = result.Outcome is "Applied" or "Duplicate"
                    ? "DELETE FROM terminal_outbox WHERE operation_id = $operation_id;"
                    : "UPDATE terminal_outbox SET state = 'Failed', error = $error WHERE operation_id = $operation_id;";
                command.Parameters.AddWithValue("$operation_id", result.OperationId.ToString("D"));
                if (result.Outcome is not ("Applied" or "Duplicate"))
                    command.Parameters.AddWithValue("$error", result.Detail ?? result.Outcome);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            transaction.Commit();
        }
        finally { _gate.Release(); }
    }

    /// <summary>How many operations still need attention.</summary>
    public async Task<int> CountOutstandingAsync(CancellationToken cancellationToken = default)
    {
        await _initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM terminal_outbox WHERE state <> 'Applied';";
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _initialization.ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}

/// <summary>An operation and its ordering position in the terminal queue.</summary>
public sealed record QueuedOperation(Guid OperationId, Guid SaleId, int Sequence, SyncOperationDto Operation);
