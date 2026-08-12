using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;

namespace VumaRetail.Sync.Dispatch;

/// <summary>
/// Runs <see cref="OutboxDispatcher"/> on a loop for the life of the host.
/// </summary>
/// <remarks>
/// <para>
/// A thin shell over the dispatcher on purpose. Everything worth asserting — batching, backoff,
/// cursor movement, what happens when the peer half-acknowledges — lives in
/// <see cref="OutboxDispatcher.DispatchOnceAsync"/>, which a test can step. What is left here is a
/// loop and a delay, and a test of a loop and a delay is a test that waits.
/// </para>
/// <para>
/// A scope per pass, because the <c>DbContext</c> and the tenant context are scoped and a long-lived
/// context accumulates every entity it has ever tracked. On a store server that runs for months, that
/// is the whole database.
/// </para>
/// <para>
/// The loop never throws out. An unhandled exception here would stop the background service for the
/// life of the process, and the first anybody would know is a week of sales missing from head office.
/// </para>
/// </remarks>
/// <param name="scopes">Creates a scope per pass.</param>
/// <param name="options">How the dispatcher is paced, and whether it runs at all.</param>
/// <param name="logger">Where the loop's own failures go.</param>
public sealed class OutboxDispatcherService(
    IServiceScopeFactory scopes,
    OutboxDispatcherOptions options,
    ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation(
                "Outbox dispatcher is disabled ({SectionName}:Enabled). Changes are captured but not shipped.",
                OutboxDispatcherOptions.SectionName);

            return;
        }

        logger.LogInformation(
            "Outbox dispatcher started: batches of {BatchSize}, idle poll every {IdleInterval}",
            options.BatchSize,
            options.IdleInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool shippedSomething = false;

            try
            {
                shippedSomething = await RunPassAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The loop must survive anything; see the class remarks.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                logger.LogError(failure, "Outbox dispatch pass failed; retrying after the idle interval");
            }

            if (shippedSomething)
            {
                // Something moved, so there may be more waiting. Come straight back rather than
                // sleeping — a store reconnecting after a day offline should drain in minutes.
                continue;
            }

            try
            {
                await Task.Delay(options.IdleInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Outbox dispatcher stopped");
    }

    private async Task<bool> RunPassAsync(CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        // The host's own tenant, set for the background pass the way the request edge sets it for a
        // request. A store server serves exactly one tenant (R2); the cloud does not run a dispatcher.
        HostTenantResolver resolver = scope.ServiceProvider.GetRequiredService<HostTenantResolver>();

        if (!resolver.TryScope(scope.ServiceProvider.GetRequiredService<ITenantContext>()))
        {
            logger.LogWarning("No host tenant is configured; the outbox dispatcher has nothing to ship");
            return false;
        }

        OutboxDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();

        return await dispatcher.DispatchOnceAsync(stoppingToken).ConfigureAwait(false) > 0;
    }
}

/// <summary>
/// Supplies the tenant a background pass runs as.
/// </summary>
/// <remarks>
/// A background job has no request to resolve a tenant from, and Stage 01's global query filter
/// treats <see cref="Guid.Empty"/> as "match nothing" — so a dispatcher that did not scope itself
/// would poll an empty outbox for ever and look perfectly healthy doing it.
/// </remarks>
/// <param name="tenantId">The tenant this host serves, or <see cref="Guid.Empty"/> where it serves many.</param>
/// <param name="storeId">The store this host serves, if any.</param>
public sealed class HostTenantResolver(Guid tenantId, Guid? storeId)
{
    /// <summary>The tenant this host serves.</summary>
    public Guid TenantId { get; } = tenantId;

    /// <summary>The store this host serves, if any.</summary>
    public Guid? StoreId { get; } = storeId;

    /// <summary>Scopes the ambient tenant context to this host's tenant.</summary>
    /// <param name="tenant">The ambient tenant context.</param>
    /// <returns>False when this host serves no single tenant, so there is nothing to scope to.</returns>
    public bool TryScope(ITenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        if (TenantId == Guid.Empty)
        {
            return false;
        }

        tenant.SetTenant(TenantId, StoreId);

        return true;
    }
}
