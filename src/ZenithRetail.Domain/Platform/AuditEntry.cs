using ZenithRetail.Domain.Entities;
using ZenithRetail.Domain.Primitives;

namespace ZenithRetail.Domain.Platform;

/// <summary>What happened to a row.</summary>
public enum AuditAction
{
    /// <summary>The row was created.</summary>
    Created = 0,

    /// <summary>One or more columns changed.</summary>
    Updated = 1,

    /// <summary>The row was soft-deleted (§7 rule 8 — nothing is hard-deleted).</summary>
    Deleted = 2,
}

/// <summary>
/// One immutable record of one change to one row. Requirement R6.
/// </summary>
/// <remarks>
/// <para>
/// Written by the persistence layer inside the same transaction as the change it describes, so a
/// change cannot commit without its audit entry and an audit entry cannot survive a rolled-back
/// change. Anything looser leaves gaps precisely when someone is looking for them.
/// </para>
/// <para>
/// It implements <see cref="IImmutableRecord"/>, so the <c>DbContext</c> refuses to save one in the
/// <c>Modified</c> or <c>Deleted</c> state. There are no mutating members on this type either — but
/// the persistence-layer guard is the load-bearing one, because it also stops a raw
/// <c>ExecuteUpdate</c> through the tracked graph.
/// </para>
/// <para>
/// <see cref="Changes"/> holds only the columns that actually changed, as JSON. Storing the whole row
/// on every update would make the audit table dwarf the data it describes within a year of trading.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class AuditEntry : Entity, IImmutableRecord
{
    private AuditEntry(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private AuditEntry()
    {
    }

    /// <summary>The CLR name of the entity that changed, for example <c>Store</c>.</summary>
    public string EntityType { get; private set; } = string.Empty;

    /// <summary>The schema-qualified table the row lives in, for example <c>platform.stores</c>.</summary>
    public string TableName { get; private set; } = string.Empty;

    /// <summary>The primary key of the row that changed.</summary>
    public Guid EntityId { get; private set; }

    /// <summary>What happened.</summary>
    public AuditAction Action { get; private set; }

    /// <summary>The principal that made the change, as reported by <c>IPrincipalAccessor</c>.</summary>
    public string Principal { get; private set; } = string.Empty;

    /// <summary>The terminal the change came from, or <c>null</c> for a server-side action.</summary>
    public Guid? TerminalId { get; private set; }

    /// <summary>
    /// True when the system made the change rather than a person — a sync receiver, a scheduled job.
    /// Recorded so an unattended change is distinguishable from a deliberate one during an investigation.
    /// </summary>
    public bool IsSystemAction { get; private set; }

    /// <summary>When the change happened, UTC, from <c>IClock</c>.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>
    /// The columns that changed, as a JSON object of <c>{ "column": { "from": …, "to": … } }</c>.
    /// </summary>
    public string Changes { get; private set; } = "{}";

    /// <summary>Records a change. Called by the persistence layer only.</summary>
    /// <param name="tenantId">The tenant the changed row belongs to.</param>
    /// <param name="storeId">The store the changed row belongs to, if any.</param>
    /// <param name="entityType">The CLR type name of the changed entity.</param>
    /// <param name="tableName">The schema-qualified table name.</param>
    /// <param name="entityId">The primary key of the changed row.</param>
    /// <param name="action">What happened.</param>
    /// <param name="principal">The acting principal.</param>
    /// <param name="terminalId">The originating terminal, if any.</param>
    /// <param name="isSystemAction">Whether the system acted rather than a person.</param>
    /// <param name="occurredAt">When it happened, UTC.</param>
    /// <param name="changes">The changed columns as JSON.</param>
    public static AuditEntry Record(
        Guid tenantId,
        Guid? storeId,
        string entityType,
        string tableName,
        Guid entityId,
        AuditAction action,
        string principal,
        Guid? terminalId,
        bool isSystemAction,
        DateTimeOffset occurredAt,
        string changes)
        => new(tenantId, storeId)
        {
            EntityType = entityType,
            TableName = tableName,
            EntityId = entityId,
            Action = action,
            Principal = principal,
            TerminalId = terminalId,
            IsSystemAction = isSystemAction,
            OccurredAt = occurredAt,
            Changes = changes,
        };
}
