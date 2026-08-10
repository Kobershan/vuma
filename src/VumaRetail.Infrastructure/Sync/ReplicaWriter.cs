using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Sync;

/// <summary>
/// Reads and writes replicated rows generically, from EF metadata.
/// </summary>
/// <remarks>
/// <para>
/// The one place in Vuma that handles an entity without knowing what it is, which makes it the one
/// place where a mistake affects every table at once. Three rules keep it honest:
/// </para>
/// <list type="number">
/// <item>
/// <b>Columns are copied from the model, not from the payload.</b> The set of properties written is
/// the receiving node's own <c>IEntityType</c>. A payload field this node does not have is ignored
/// rather than an error — a store running last month's build must keep replicating — and a field the
/// sender omitted keeps its local value rather than becoming null.
/// </item>
/// <item>
/// <b><see cref="Entity.RowVersion"/> is never copied.</b> It is a node-local concurrency token
/// (ADR-035) and copying the sender's would make the receiving node's next optimistic-concurrency
/// check compare against a value its own database never issued.
/// </item>
/// <item>
/// <b>The sender's stamp is written to the row, not a fresh one.</b> A receiver that re-stamped would
/// make every applied change look newer than the original, and the next comparison in the other
/// direction would undo it — the classic replication oscillation.
/// </item>
/// </list>
/// </remarks>
/// <param name="context">The database context whose model and change tracker are used.</param>
/// <param name="scope">Records each row written here, so the outbox does not send it straight back.</param>
public sealed class ReplicaWriter(VumaRetailDbContext context, IReplicationScope scope) : IReplicaWriter
{
    /// <summary>
    /// Columns the replication layer owns and never takes from a payload.
    /// </summary>
    /// <remarks>
    /// <see cref="Entity.RowVersion"/> is node-local (ADR-035). <see cref="Entity.SyncState"/>
    /// describes this node's view of the row's journey and means nothing on another node.
    /// <see cref="Entity.SyncStamp"/> is set explicitly from the operation.
    /// </remarks>
    private static readonly HashSet<string> NotCopied = new(StringComparer.Ordinal)
    {
        nameof(Entity.RowVersion),
        nameof(Entity.SyncState),
        nameof(Entity.SyncStamp),
    };

    /// <inheritdoc />
    public async Task<ReplicaRow?> FindAsync(
        Type entityType,
        Guid entityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        Entity? entity = await FindTrackedAsync(entityType, entityId, cancellationToken).ConfigureAwait(false);

        return entity is null
            ? null
            : new ReplicaRow(Serialise(entity), entity.Stamp, entity.IsDeleted);
    }

    /// <inheritdoc />
    public async Task ApplyAsync(
        Type entityType,
        SyncOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(operation);

        JsonObject payload = ParsePayload(operation);

        Entity? existing = await FindTrackedAsync(entityType, operation.EntityId, cancellationToken)
            .ConfigureAwait(false);

        Entity target = existing ?? Materialise(entityType);
        EntityEntry entry = existing is null ? context.Add(target) : context.Entry(target);

        CopyProperties(entry, payload, isNew: existing is null);

        target.MarkSyncStamp(operation.Stamp);
        target.MarkSyncState(SyncState.Synced);

        // Recorded here rather than by the receiver, because this is the only place that knows a row
        // was written on a peer's instruction. Without it the outbox behaviour captures the change
        // and sends it straight back where it came from.
        scope.RecordApplied(target.Id);

        if (operation.Operation == SyncOperationKind.Delete && !target.IsDeleted)
        {
            // §7 rule 8 holds on every tier. A replicated delete is a soft delete, applied through
            // the same method local code uses, so the audit interceptor records it as a deletion
            // rather than as an update that happened to set a column.
            //
            // Only when the payload did not already carry the deletion. The sending node stamps
            // before it serialises, so a delete usually arrives with the originating node's
            // deleted_at and deleted_by already on it — and the person who deleted the record is a
            // better answer to R6's "who" than the name of the machine that replicated it.
            target.MarkDeleted(DeletedByReplication, operation.OccurredAt);
        }
    }

    /// <inheritdoc />
    public string Serialise(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        EntityEntry entry = context.Entry(entity);
        JsonObject payload = [];

        foreach (PropertyEntry property in entry.Properties)
        {
            if (NotCopied.Contains(property.Metadata.Name))
            {
                continue;
            }

            payload[property.Metadata.Name] = ToJson(property.CurrentValue);
        }

        return payload.ToJsonString();
    }

    /// <summary>The principal recorded on a row soft-deleted by a replicated operation (R6).</summary>
    private const string DeletedByReplication = "system:sync";

    private async Task<Entity?> FindTrackedAsync(
        Type entityType,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        // The change tracker first. Two operations in one batch can target the same row, and a
        // second database round trip would return the version from before the first one applied.
        if (context.ChangeTracker.Entries<Entity>()
                .FirstOrDefault(entry => entry.Metadata.ClrType == entityType && entry.Entity.Id == entityId)
            is { } tracked)
        {
            return tracked.Entity;
        }

        MethodInfo query = QueryMethods.GetOrAdd(
            entityType,
            static type => LoadMethod.MakeGenericMethod(type));

        return await ((Task<Entity?>)query.Invoke(this, [entityId, cancellationToken])!).ConfigureAwait(false);
    }

    private static readonly MethodInfo LoadMethod = typeof(ReplicaWriter)
        .GetMethod(nameof(LoadAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly ConcurrentDictionary<Type, MethodInfo> QueryMethods = new();

    /// <summary>
    /// Loads one row by id, ignoring the soft-delete filter and applying the tenant filter by hand.
    /// </summary>
    /// <remarks>
    /// <c>IgnoreQueryFilters</c> drops both of Stage 01's global filters at once, so the tenant
    /// predicate is written back explicitly on the next line. The soft-delete filter has to go: a
    /// node that cannot see its own tombstone re-creates the row every time a stale upsert for it is
    /// replayed, which is a deleted product coming back on the shelf after every reconnect. The
    /// tenant filter must not, ever.
    /// </remarks>
    private async Task<Entity?> LoadAsync<TEntity>(Guid entityId, CancellationToken cancellationToken)
        where TEntity : Entity
        => await context.Set<TEntity>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                entity => entity.Id == entityId && entity.TenantId == context.CurrentTenantId,
                cancellationToken)
            .ConfigureAwait(false);

    private static Entity Materialise(Type entityType)
        // EF's entity types keep a parameterless constructor for materialisation, and it is private
        // by convention here so business code cannot reach it. This is the one caller that must.
        => (Entity)(Activator.CreateInstance(entityType, nonPublic: true)
            ?? throw new InvalidOperationException(
                $"{entityType.Name} has no parameterless constructor, so a replicated row of it "
                + "cannot be materialised. Every entity needs the private EF constructor."));

    private static JsonObject ParsePayload(SyncOperation operation)
    {
        try
        {
            return JsonNode.Parse(operation.Payload) as JsonObject
                ?? throw new MalformedReplicaPayloadException(operation.EntityType, operation.EntityId);
        }
        catch (JsonException failure)
        {
            throw new MalformedReplicaPayloadException(operation.EntityType, operation.EntityId, failure);
        }
    }

    private void CopyProperties(EntityEntry entry, JsonObject payload, bool isNew)
    {
        foreach (IProperty property in entry.Metadata.GetProperties())
        {
            string name = property.Name;

            if (NotCopied.Contains(name))
            {
                continue;
            }

            // CreatedAt and CreatedBy are copied on insert and never on update — the audit
            // interceptor already refuses to write them on an update, and the origin of a row is the
            // originating node's fact, not the receiver's.
            if (!isNew && name is nameof(Entity.CreatedAt) or nameof(Entity.CreatedBy))
            {
                continue;
            }

            if (!payload.TryGetPropertyValue(name, out JsonNode? value))
            {
                // The sender does not have this column — an older build. Keeping the local value is
                // the only safe answer; writing null would silently blank a column on upgrade.
                continue;
            }

            entry.Property(name).CurrentValue = FromJson(value, property.ClrType);
        }
    }

    private static JsonNode? ToJson(object? value) => value switch
    {
        null => null,
        string text => JsonValue.Create(text),
        // The only byte[] on an entity is the concurrency token, which is never copied — but an
        // entity could gain one, and base64 is the honest representation.
        byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
        _ => JsonSerializer.SerializeToNode(value, value.GetType(), SerialiserOptions),
    };

    private static object? FromJson(JsonNode? value, Type clrType)
    {
        if (value is null)
        {
            return null;
        }

        Type target = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (target == typeof(byte[]))
        {
            return Convert.FromBase64String(value.GetValue<string>());
        }

        object? deserialised = JsonSerializer.Deserialize(value.ToJsonString(), target, SerialiserOptions);

        return deserialised;
    }

    private static readonly JsonSerializerOptions SerialiserOptions = new()
    {
        WriteIndented = false,
    };
}

/// <summary>Thrown when a replicated operation's payload is not a JSON object.</summary>
/// <param name="entityTypeName">The entity type the operation targets.</param>
/// <param name="entityId">The row the operation targets.</param>
/// <param name="innerException">The parse failure, where there was one.</param>
public sealed class MalformedReplicaPayloadException(
    string entityTypeName,
    Guid entityId,
    Exception? innerException = null)
    : DomainException(
        "SYNC_PAYLOAD_MALFORMED",
        $"The payload for {entityTypeName} {entityId} is not a JSON object.",
        DomainProblemKind.Malformed)
{
    /// <summary>The parse failure, where there was one.</summary>
    public Exception? Cause { get; } = innerException;
}
