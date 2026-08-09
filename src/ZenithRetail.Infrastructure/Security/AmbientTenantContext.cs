using Microsoft.Extensions.Logging;
using ZenithRetail.Application.Abstractions;

namespace ZenithRetail.Infrastructure.Security;

/// <summary>
/// The default <see cref="ITenantContext"/>: scoped per request, per POS operation, per sync batch.
/// </summary>
/// <remarks>
/// Registered scoped, so the ambient tenant is whatever the current unit of work resolved and cannot
/// leak into a concurrent one. Stage 02 sets it from the authenticated principal at the request edge;
/// Stage 04's sync receiver sets it per batch.
/// </remarks>
/// <param name="logger">Records every crossing of the tenant boundary.</param>
public sealed class AmbientTenantContext(ILogger<AmbientTenantContext> logger) : ITenantContext
{
    private int _bypassDepth;

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public Guid? StoreId { get; private set; }

    /// <inheritdoc />
    public bool IsFilterBypassed => _bypassDepth > 0;

    /// <inheritdoc />
    public void SetTenant(Guid tenantId, Guid? storeId = null)
    {
        TenantId = tenantId;
        StoreId = storeId;
    }

    /// <inheritdoc />
    public IDisposable BypassTenantFilter(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        // Logged at warning, not debug. Only two callers are meant to reach this — the cloud tier's
        // sync receiver and the backup job — so a third appearing in a log is exactly the thing
        // somebody should notice.
        logger.LogWarning("Tenant query filter bypassed: {Reason}", reason);

        _bypassDepth++;
        return new BypassScope(this);
    }

    private void EndBypass()
    {
        if (_bypassDepth > 0)
        {
            _bypassDepth--;
        }
    }

    private sealed class BypassScope(AmbientTenantContext owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.EndBypass();
        }
    }
}
