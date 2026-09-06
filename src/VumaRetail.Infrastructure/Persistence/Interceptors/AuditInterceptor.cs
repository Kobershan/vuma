using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Platform;

namespace VumaRetail.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes the audit trail for everything on its way to the database, in the same transaction (R6).
/// </summary>
/// <remarks>
/// <para>
/// Four rules land here: nothing is hard-deleted (§7 rule 8), immutable records stay immutable
/// (§7 rule 7 / ADR-012), audit fields are stamped by the persistence layer and never by business
/// code (R6), and every change writes an audit entry in the same transaction as the change itself.
/// </para>
/// <para>
/// The first three moved into <see cref="AuditStamper"/> in Stage 04 so the outbox behaviour can ask
/// for them <em>before</em> it serialises a row for replication. They still happen here for anything
/// the behaviour did not stamp — a query-side save, a background job, the demo seeder — and calling
/// the stamper twice in one save stamps once. What is left in this class is the trail itself, which
/// must be built after stamping so each entry carries the same principal and instant as the change
/// it describes.
/// </para>
/// </remarks>
/// <param name="stamper">Applies the audit stamp and classifies each change.</param>
public sealed class AuditInterceptor(AuditStamper stamper) : SaveChangesInterceptor
{
    /// <summary>
    /// Audit columns are excluded from the recorded diff. Every update touches them, so including
    /// them would bury the one column that actually changed under five that always do.
    /// </summary>
    private static readonly HashSet<string> AuditColumns = new(StringComparer.Ordinal)
    {
        nameof(Entity.CreatedAt),
        nameof(Entity.CreatedBy),
        nameof(Entity.UpdatedAt),
        nameof(Entity.UpdatedBy),
        nameof(Entity.RowVersion),
        nameof(Entity.SyncState),
        nameof(Entity.SyncStamp),
    };

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        IReadOnlyList<StampedChange> changes = stamper.Stamp(context);

        DateTimeOffset now = stamper.Now;
        string principal = stamper.Principal;
        Guid? terminalId = stamper.TerminalId;
        bool isSystem = stamper.IsSystem;

        List<AuditEntry> trail = [];

        foreach ((EntityEntry<Entity> entry, AuditAction action) in changes)
        {
            trail.Add(AuditEntry.Record(
                entry.Entity.TenantId,
                entry.Entity.StoreId,
                entry.Entity.GetType().Name,
                DescribeTable(entry),
                entry.Entity.Id,
                action,
                // This node's principal, even for a row applied from a peer whose own updated_by
                // keeps the originating editor. The row says who changed the record; the trail says
                // what this machine did about it, and both questions get asked in an investigation.
                principal,
                terminalId,
                isSystem,
                now,
                SerialiseChanges(entry, action)));
        }

        foreach (AuditEntry auditEntry in trail)
        {
            // Stamped here rather than by a second pass: an audit entry is itself a row and needs its
            // §7 rule 3 columns, but it must not produce an audit entry of its own.
            auditEntry.MarkCreated(principal, now);
            // Audit rows are created during interception, after VumaRetailDbContext's normal
            // company-stamping pass. They belong to the same company as the changed row.
            auditEntry.AssignCompany(changes.First(change => change.Entry.Entity.Id == auditEntry.EntityId).Entry.Entity.CompanyId ?? Guid.Empty);
            auditEntry.SetRowVersion(AuditStamper.NewRowVersion());
            context.Add(auditEntry);
        }

        stamper.Complete();
    }

    private static string DescribeTable(EntityEntry<Entity> entry)
    {
        Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType = entry.Metadata;
        string? schema = entityType.GetSchema();
        string table = entityType.GetTableName() ?? entityType.ClrType.Name;

        return schema is null ? table : $"{schema}.{table}";
    }

    private static string SerialiseChanges(EntityEntry<Entity> entry, AuditAction action)
    {
        JsonObject changes = [];

        foreach (PropertyEntry property in entry.Properties)
        {
            string name = property.Metadata.Name;

            if (AuditColumns.Contains(name))
            {
                continue;
            }

            // On an insert there is no "before", so record the values the row was created with —
            // otherwise the trail can say a row exists but never what it originally said.
            if (action is AuditAction.Created)
            {
                if (property.CurrentValue is not null)
                {
                    changes[name] = ToJson(property.CurrentValue);
                }

                continue;
            }

            if (!property.IsModified || Equals(property.OriginalValue, property.CurrentValue))
            {
                continue;
            }

            changes[name] = new JsonObject
            {
                ["from"] = ToJson(property.OriginalValue),
                ["to"] = ToJson(property.CurrentValue),
            };
        }

        return changes.ToJsonString();
    }

    private static JsonNode? ToJson(object? value) => value switch
    {
        null => null,
        // byte[] would serialise as a base64 blob nobody can read in an investigation, and the only
        // byte[] on an entity is the concurrency token, which carries no business meaning anyway.
        byte[] => "<binary>",
        string text => JsonValue.Create(text),
        _ => JsonNode.Parse(JsonSerializer.Serialize(value, value.GetType(), SerialiserOptions)),
    };

    private static readonly JsonSerializerOptions SerialiserOptions = new()
    {
        WriteIndented = false,
    };
}
