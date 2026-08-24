namespace VumaRetail.Application.Abstractions.Identity;

/// <summary>
/// Whether a user holds a permission — the in-handler counterpart to the endpoint's
/// <c>RequirePermission</c> gate, for the rare write whose legality depends on state only the handler
/// can see (<c>PROGRESS.md</c> §4.15).
/// </summary>
/// <remarks>
/// Almost every permission check belongs at the endpoint and stays there. This port exists for the
/// handful of commands where "is this allowed" cannot be answered from the request alone — a reprint
/// is <c>pos.receipt.print</c> until a prior print row exists, at which point it is
/// <c>pos.receipt.reprint</c>, and an endpoint filter sees the request before any row is read. A module
/// reaching for this on every write is a module that should have narrowed its endpoint permission
/// instead.
/// </remarks>
public interface IPermissionChecker
{
    /// <summary>True when the user's effective permissions, in the given store, include this one.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="storeId">The store being acted in, or <c>null</c> for a tenant-wide action.</param>
    /// <param name="permission">The permission key, for example <c>pos.receipt.reprint</c>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> HasPermissionAsync(
        Guid userId, Guid? storeId, string permission, CancellationToken cancellationToken = default);
}
