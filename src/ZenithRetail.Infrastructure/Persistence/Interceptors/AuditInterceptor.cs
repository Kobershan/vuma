using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ZenithRetail.Application.Abstractions;
using ZenithRetail.Domain.Entities;
using ZenithRetail.Domain.Platform;

namespace ZenithRetail.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Everything that must happen to a row on its way to the database, in one pass, in a fixed order.
/// </summary>
/// <remarks>
/// <para>
/// Four rules land here: nothing is hard-deleted (§7 rule 8), immutable records stay immutable
/// (§7 rule 7 / ADR-012), audit fields are stamped by the persistence layer and never by business
/// code (R6), and every change writes an audit entry in the same transaction as the change itself.
/// </para>
/// <para>
/// They are one class rather than four interceptors because the order between them is load-bearing
/// and easy to get wrong from a registration list: a delete must become a soft delete <em>before</em>
/// the audit entry decides what happened, and the audit entry must be built <em>after</em> stamping
/// so its own row carries the same principal and instant. Split across four registrations, that
/// ordering is invisible at the point somebody reorders them.
/// </para>
/// </remarks>
/// <param name="clock">The only source of time — never the wall clock, so ladders and expiries are testable.</param>
/// <param name="principalAccessor">Who is acting.</param>
public sealed class AuditInterceptor(IClock clock, IPrincipalAccessor principalAccessor) : SaveChangesInterceptor
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

        DateTimeOffset now = clock.UtcNow;
        string principal = principalAccessor.Principal;
        Guid? terminalId = principalAccessor.TerminalId;
        bool isSystem = principalAccessor.IsSystem;

        List<EntityEntry<Entity>> entries = context.ChangeTracker
            .Entries<Entity>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        List<AuditEntry> trail = [];

        foreach (EntityEntry<Entity> entry in entries)
        {
            GuardImmutable(entry);
            RewriteHardDeleteAsSoftDelete(entry, principal, now);

            AuditAction action = ClassifyAndStamp(entry, principal, now);

            trail.Add(AuditEntry.Record(
                entry.Entity.TenantId,
                entry.Entity.StoreId,
                entry.Entity.GetType().Name,
                DescribeTable(entry),
                entry.Entity.Id,
                action,
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
            auditEntry.SetRowVersion(NewRowVersion());
            context.Add(auditEntry);
        }
    }

    private static void GuardImmutable(EntityEntry<Entity> entry)
    {
        if (entry.Entity is not IImmutableRecord || entry.State is EntityState.Added)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{entry.Entity.GetType().Name} is an immutable record and cannot be "
            + $"{(entry.State is EntityState.Deleted ? "deleted" : "modified")} once written. "
            + "Correct it with a new reversing record instead (CLAUDE.md §7 rule 7, ADR-012).");
    }

    private static void RewriteHardDeleteAsSoftDelete(EntityEntry<Entity> entry, string principal, DateTimeOffset now)
    {
        if (entry.State is not EntityState.Deleted)
        {
            return;
        }

        // §7 rule 8. Rewritten rather than thrown on: a caller reaching for Remove means "make this go
        // away", and the persistence layer is the thing that knows how that is done here. Throwing
        // would only train modules to write the soft delete by hand, which is where it gets forgotten.
        entry.State = EntityState.Modified;
        entry.Entity.MarkDeleted(principal, now);
    }

    private static AuditAction ClassifyAndStamp(EntityEntry<Entity> entry, string principal, DateTimeOffset now)
    {
        if (entry.State is EntityState.Added)
        {
            entry.Entity.MarkCreated(principal, now);
            entry.Entity.SetRowVersion(NewRowVersion());
            return AuditAction.Created;
        }

        bool isSoftDelete = entry.Property(e => e.DeletedAt).IsModified && entry.Entity.DeletedAt is not null;

        entry.Entity.MarkUpdated(principal, now);
        entry.Entity.SetRowVersion(NewRowVersion());

        // CreatedAt/CreatedBy are set once and never again. EF would happily write whatever is on the
        // instance, so a materialised-then-detached entity could otherwise rewrite its own origin.
        entry.Property(e => e.CreatedAt).IsModified = false;
        entry.Property(e => e.CreatedBy).IsModified = false;

        return isSoftDelete ? AuditAction.Deleted : AuditAction.Updated;
    }

    /// <summary>
    /// A fresh concurrency token. See ADR-035 — an application-generated <c>bytea</c> rather than
    /// PostgreSQL's <c>xmin</c>, because the same rows are cached in SQLite on every terminal and
    /// SQLite has no <c>xmin</c> to compare against.
    /// </summary>
    private static byte[] NewRowVersion() => Guid.NewGuid().ToByteArray();

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
