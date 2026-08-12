using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Platform;

namespace VumaRetail.Infrastructure.Persistence.Interceptors;

/// <summary>One changed entity, stamped, with what happened to it.</summary>
/// <param name="Entry">The change tracker entry.</param>
/// <param name="Action">What happened, after any hard delete was rewritten as a soft delete.</param>
public readonly record struct StampedChange(EntityEntry<Entity> Entry, AuditAction Action);

/// <summary>
/// Applies the audit stamp to everything pending on a context, once per save, on demand.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <see cref="AuditInterceptor"/> in Stage 04 because of a real ordering bug. The
/// interceptor runs at <c>SavingChanges</c>, which is <em>after</em> the outbox behaviour in pipeline
/// slot 300 has already serialised each changed row into its outbox payload. So every replicated row
/// left its origin node carrying <c>created_by = ""</c> and <c>created_at = 0001-01-01</c>, and every
/// other tier stored those blanks — R6's "who changed what, when" was true only on the machine where
/// the change was made, which is the one place nobody needs to ask.
/// </para>
/// <para>
/// The fix is to let the outbox behaviour stamp first and the interceptor stamp what is left. This
/// class is what makes that safe: calling it twice in one save stamps once. The second call still
/// reports every change, because the interceptor needs the full list to write the trail.
/// </para>
/// <para>
/// Scoped, and the marks are cleared by <see cref="Complete"/> at the end of each save. A command
/// that changes the same entity twice in one scope is stamped twice, once per save, which is
/// correct — what must not happen is two stamps for one save.
/// </para>
/// </remarks>
/// <param name="clock">The only source of time — never the wall clock, so ladders and expiries are testable.</param>
/// <param name="principalAccessor">Who is acting.</param>
/// <param name="replication">
/// Which rows arrived from a peer. Those are stamped by their originating node and must keep it.
/// </param>
public sealed class AuditStamper(
    IClock clock,
    IPrincipalAccessor principalAccessor,
    IReplicationScope replication)
{
    /// <summary>
    /// The entities stamped so far in this save, by reference.
    /// </summary>
    /// <remarks>
    /// By reference rather than by <c>Id</c>: two tracked instances of one row cannot exist in a
    /// single context, and an entity that has not been assigned its id yet would otherwise collide
    /// with every other unassigned one on <see cref="Guid.Empty"/>.
    /// </remarks>
    private readonly HashSet<Entity> _stamped = new(EntityReferenceComparer.Instance);

    /// <summary>Who is acting, as written to <c>created_by</c> and <c>updated_by</c>.</summary>
    public string Principal => principalAccessor.Principal;

    /// <summary>The terminal the action came from, or <c>null</c> for a server-side action (R6).</summary>
    public Guid? TerminalId => principalAccessor.TerminalId;

    /// <summary>Whether the system is acting rather than a person.</summary>
    public bool IsSystem => principalAccessor.IsSystem;

    /// <summary>The instant this save is stamped with.</summary>
    public DateTimeOffset Now => clock.UtcNow;

    /// <summary>
    /// Stamps every pending change and reports what happened to each.
    /// </summary>
    /// <param name="context">The context whose change tracker is stamped.</param>
    /// <returns>Every pending change, with its classification.</returns>
    /// <exception cref="InvalidOperationException">An immutable record was modified or deleted.</exception>
    public IReadOnlyList<StampedChange> Stamp(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        DateTimeOffset now = clock.UtcNow;
        string principal = principalAccessor.Principal;

        List<StampedChange> changes = [];

        foreach (EntityEntry<Entity> entry in context.ChangeTracker
            .Entries<Entity>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList())
        {
            GuardImmutable(entry);
            RewriteHardDeleteAsSoftDelete(entry, principal, now);

            changes.Add(new StampedChange(entry, ClassifyAndStamp(entry, principal, now)));
        }

        return changes;
    }

    /// <summary>Clears the per-save marks. Called once the save's changes have been written.</summary>
    public void Complete() => _stamped.Clear();

    /// <summary>A fresh concurrency token (ADR-035).</summary>
    /// <remarks>
    /// An application-generated <c>bytea</c> rather than PostgreSQL's <c>xmin</c>, because the same
    /// rows are cached in SQLite on every terminal (ADR-003) and SQLite has no <c>xmin</c> to compare
    /// against. Regenerated on <em>every</em> node that writes the row, replicated or not: it is
    /// node-local by definition.
    /// </remarks>
    public static byte[] NewRowVersion() => Guid.NewGuid().ToByteArray();

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

    private void RewriteHardDeleteAsSoftDelete(EntityEntry<Entity> entry, string principal, DateTimeOffset now)
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

    private AuditAction ClassifyAndStamp(EntityEntry<Entity> entry, string principal, DateTimeOffset now)
    {
        // The classification is computed on every call, because the interceptor needs it to write the
        // trail even when the outbox behaviour already did the stamping.
        bool alreadyStamped = !_stamped.Add(entry.Entity);

        if (entry.State is EntityState.Added)
        {
            // A row applied from a peer arrives carrying the originating node's principal and
            // instant, and that origin is R6's answer to "who". Overwriting it would make every
            // replicated row look as though this node's sync receiver had created it.
            if (!alreadyStamped && !IsFromAPeer(entry) && string.IsNullOrEmpty(entry.Entity.CreatedBy))
            {
                entry.Entity.MarkCreated(principal, now);
            }

            entry.Entity.SetRowVersion(NewRowVersion());

            return AuditAction.Created;
        }

        bool isSoftDelete = entry.Property(entity => entity.DeletedAt).IsModified
            && entry.Entity.DeletedAt is not null;

        if (!alreadyStamped && !IsFromAPeer(entry))
        {
            entry.Entity.MarkUpdated(principal, now);
        }

        entry.Entity.SetRowVersion(NewRowVersion());

        // CreatedAt/CreatedBy are set once and never again. EF would happily write whatever is on the
        // instance, so a materialised-then-detached entity could otherwise rewrite its own origin.
        entry.Property(e => e.CreatedAt).IsModified = false;
        entry.Property(e => e.CreatedBy).IsModified = false;

        return isSoftDelete ? AuditAction.Deleted : AuditAction.Updated;
    }

    /// <summary>
    /// Whether this row was written because a peer sent it, rather than by something on this node.
    /// </summary>
    /// <remarks>
    /// The row keeps the originating node's <c>updated_by</c>; the audit entry this save writes still
    /// records <em>this</em> node's principal and <c>is_system_action</c>. That split is deliberate:
    /// the row says who changed the record, the trail says what this machine did about it.
    /// </remarks>
    private bool IsFromAPeer(EntityEntry<Entity> entry)
        => replication.AppliedRowIds.Contains(entry.Entity.Id);

    /// <summary>Compares tracked entities by reference, so identity does not depend on a set id.</summary>
    private sealed class EntityReferenceComparer : IEqualityComparer<Entity>
    {
        public static readonly EntityReferenceComparer Instance = new();

        public bool Equals(Entity? left, Entity? right) => ReferenceEquals(left, right);

        public int GetHashCode(Entity entity) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(entity);
    }
}
