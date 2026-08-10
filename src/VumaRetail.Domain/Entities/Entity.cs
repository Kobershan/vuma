using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Entities;

/// <summary>
/// The base every persisted entity inherits. Carries the columns CLAUDE.md §7 rule 3 requires on
/// every table, so no module can forget one.
/// </summary>
/// <remarks>
/// <para>
/// Rule 3 (mandatory columns), rule 8 (nothing is hard-deleted) and rule 9 (all timestamps are UTC
/// <c>timestamptz</c>) are all enforced from here. Stage 01 maps these to columns once, in the EF
/// base configuration, rather than per entity.
/// </para>
/// <para>
/// Setters are <c>protected</c>: audit fields are stamped by the persistence layer and the command
/// pipeline, never by business code. An entity that sets its own <see cref="UpdatedBy"/> is writing
/// an audit trail it controls, which is not an audit trail.
/// </para>
/// </remarks>
public abstract class Entity
{
    /// <summary>Creates an entity with a fresh UUID v7 identity.</summary>
    /// <param name="tenantId">The owning tenant. Required — every row is tenant-scoped.</param>
    /// <param name="storeId">The owning store, where the entity belongs to one.</param>
    protected Entity(Guid tenantId, Guid? storeId = null)
    {
        Id = UuidV7.NewGuid();
        TenantId = tenantId;
        StoreId = storeId;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    protected Entity()
    {
    }

    /// <summary>UUID v7 primary key, generated client-side so offline terminals never wait (ADR-004).</summary>
    public Guid Id { get; protected set; }

    /// <summary>The owning tenant. Every query is filtered by this.</summary>
    public Guid TenantId { get; protected set; }

    /// <summary>The owning store, or <c>null</c> for tenant-wide records such as the chart of accounts.</summary>
    public Guid? StoreId { get; protected set; }

    /// <summary>When the row was created, UTC.</summary>
    public DateTimeOffset CreatedAt { get; protected set; }

    /// <summary>The user or system principal that created the row.</summary>
    public string CreatedBy { get; protected set; } = string.Empty;

    /// <summary>When the row was last changed, UTC.</summary>
    public DateTimeOffset UpdatedAt { get; protected set; }

    /// <summary>The user or system principal that last changed the row.</summary>
    public string UpdatedBy { get; protected set; } = string.Empty;

    /// <summary>Optimistic concurrency token, mapped to <c>xmin</c> in PostgreSQL.</summary>
    public byte[] RowVersion { get; protected set; } = [];

    /// <summary>Where the row stands in replication (ADR-006).</summary>
    public SyncState SyncState { get; protected set; } = SyncState.Local;

    /// <summary>When the row was soft-deleted, or <c>null</c> if it is live (rule 8).</summary>
    public DateTimeOffset? DeletedAt { get; protected set; }

    /// <summary>The principal that soft-deleted the row.</summary>
    public string? DeletedBy { get; protected set; }

    /// <summary>True once the row has been soft-deleted. Filtered out by a global query filter.</summary>
    public bool IsDeleted => DeletedAt is not null;

    /// <summary>
    /// Stamps creation audit fields. Called by the persistence layer on insert.
    /// </summary>
    /// <param name="principal">The acting user or system principal.</param>
    /// <param name="timestamp">The instant of the change, UTC.</param>
    public void MarkCreated(string principal, DateTimeOffset timestamp)
    {
        CreatedBy = principal;
        CreatedAt = timestamp;
        UpdatedBy = principal;
        UpdatedAt = timestamp;
    }

    /// <summary>
    /// Stamps update audit fields. Called by the persistence layer on update.
    /// </summary>
    /// <param name="principal">The acting user or system principal.</param>
    /// <param name="timestamp">The instant of the change, UTC.</param>
    public void MarkUpdated(string principal, DateTimeOffset timestamp)
    {
        UpdatedBy = principal;
        UpdatedAt = timestamp;
    }

    /// <summary>
    /// Soft-deletes the row (rule 8 — nothing is hard-deleted).
    /// </summary>
    /// <param name="principal">The acting user or system principal.</param>
    /// <param name="timestamp">The instant of the deletion, UTC.</param>
    public void MarkDeleted(string principal, DateTimeOffset timestamp)
    {
        DeletedAt = timestamp;
        DeletedBy = principal;
        UpdatedBy = principal;
        UpdatedAt = timestamp;
    }

    /// <summary>Records progress through the replication chain.</summary>
    /// <param name="state">The new replication state.</param>
    public void MarkSyncState(SyncState state) => SyncState = state;

    /// <summary>
    /// Replaces the optimistic concurrency token. Called by the persistence layer on every insert and
    /// update; business code has no reason to.
    /// </summary>
    /// <param name="token">The new token.</param>
    /// <remarks>
    /// ADR-035: the token is generated by the application rather than by PostgreSQL's <c>xmin</c>,
    /// because the same rows are cached in SQLite on every terminal (ADR-003) and SQLite has no
    /// <c>xmin</c>. Sync compares tokens across both tiers, so they have to be the same kind of thing.
    /// </remarks>
    public void SetRowVersion(byte[] token) => RowVersion = token;
}
