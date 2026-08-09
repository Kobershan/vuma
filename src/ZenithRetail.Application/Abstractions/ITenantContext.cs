namespace ZenithRetail.Application.Abstractions;

/// <summary>
/// The ambient tenant and store every query is filtered by.
/// </summary>
/// <remarks>
/// <para>
/// <c>CLAUDE.md</c> §7 rule 3 puts <c>tenant_id</c> on every table; this port is what makes that
/// column do something. The <c>DbContext</c> applies a global query filter from
/// <see cref="TenantId"/>, so tenant isolation is the default behaviour of the persistence layer
/// rather than a <c>Where</c> clause each module has to remember. R8 (multi-store, multi-tenant from
/// the data model up) depends on it holding without exception.
/// </para>
/// <para>
/// The escape hatch is deliberately awkward. Two callers legitimately cross tenants — the cloud
/// tier's sync receiver, which lands batches for every store it replicates, and the backup job. Both
/// must ask for it in a <c>using</c> block that names itself, so crossing the boundary is a visible
/// act in a diff rather than a missing filter nobody noticed.
/// </para>
/// </remarks>
public interface ITenantContext
{
    /// <summary>
    /// The tenant every query is scoped to, or <see cref="Guid.Empty"/> before a tenant is resolved —
    /// during activation, at the login screen, or inside a bypass scope.
    /// </summary>
    Guid TenantId { get; }

    /// <summary>The store the caller is acting in, or <c>null</c> for a tenant-wide action.</summary>
    Guid? StoreId { get; }

    /// <summary>True while a <see cref="BypassTenantFilter"/> scope is open.</summary>
    bool IsFilterBypassed { get; }

    /// <summary>Sets the ambient tenant and store. Called at the request edge, not from a handler.</summary>
    /// <param name="tenantId">The tenant to scope to.</param>
    /// <param name="storeId">The store to scope to, where the caller is acting in one.</param>
    void SetTenant(Guid tenantId, Guid? storeId = null);

    /// <summary>
    /// Opens a scope in which the tenant query filter does not apply.
    /// </summary>
    /// <param name="reason">
    /// Why the boundary is being crossed, written to the log. Required — an unexplained cross-tenant
    /// read is the thing this method exists to make visible.
    /// </param>
    /// <returns>A scope that restores tenant filtering when disposed.</returns>
    IDisposable BypassTenantFilter(string reason);
}
