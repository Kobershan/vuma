using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Licensing.Commands;

namespace VumaRetail.Licensing.Hosting;

/// <summary>
/// The tenant a licensing background pass runs as, and the store it runs for.
/// </summary>
/// <remarks>
/// A hosted service has no request and therefore no ambient tenant. The same shape Stage 04's outbox
/// dispatcher uses: the host says which tenant its background work belongs to, and the pass scopes
/// itself rather than bypassing the tenant filter.
/// </remarks>
/// <param name="TenantId">The tenant this host serves.</param>
/// <param name="StoreId">The store this host runs, where it runs one.</param>
public sealed record LicensingHostTenant(Guid TenantId, Guid? StoreId);

/// <summary>
/// Keeps the lease fresh and the heartbeat beating.
/// </summary>
/// <remarks>
/// <para>
/// One service for both, because they are the same conversation and pacing them together is what makes
/// the sixty-second recovery promise achievable: while a tenant is restricted the interval drops from
/// five minutes to thirty seconds, so a customer who has just paid does not sit watching a till that
/// will find out at some point.
/// </para>
/// <para>
/// <b>Nothing here ever throws its way into an enforcement decision.</b> A control plane that is down
/// produces a logged debug line and another pass; that is the whole handling, and it is the shape
/// ADR-028's fourth rule requires.
/// </para>
/// </remarks>
/// <param name="services">The root container; each pass takes its own scope.</param>
/// <param name="options">The intervals.</param>
/// <param name="host">The tenant a pass runs as.</param>
/// <param name="logger">Where a failed pass is recorded.</param>
public sealed class LicenceMaintenanceService(
    IServiceProvider services,
    LicensingOptions options,
    LicensingHostTenant host,
    ILogger<LicenceMaintenanceService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateTimeOffset lastLeaseRefresh = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait = options.HeartbeatInterval;

            try
            {
                (bool restricted, DateTimeOffset now) = await RunPassAsync(
                    lastLeaseRefresh,
                    stoppingToken).ConfigureAwait(false);

                if (now - lastLeaseRefresh >= options.LeaseRefreshInterval)
                {
                    lastLeaseRefresh = now;
                }

                wait = restricted ? options.RestrictedHeartbeatInterval : options.HeartbeatInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (NotActivatedException)
            {
                // Nothing to maintain yet. The activation wizard is what fixes this, and a store
                // waiting to be activated should not fill its log with the fact.
                wait = options.HeartbeatInterval;
            }
#pragma warning disable CA1031 // A background pass must survive anything a pass can throw.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                logger.LogWarning(failure, "Licence maintenance pass failed; retrying at the next interval.");
            }

            try
            {
                await Task.Delay(wait, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<(bool Restricted, DateTimeOffset Now)> RunPassAsync(
        DateTimeOffset lastLeaseRefresh,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = services.CreateScope();

        ITenantContext tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.SetTenant(host.TenantId, host.StoreId);

        IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        IClock clock = scope.ServiceProvider.GetRequiredService<IClock>();

        DateTimeOffset now = clock.UtcNow;

        if (now - lastLeaseRefresh >= options.LeaseRefreshInterval)
        {
            await dispatcher.SendAsync(new RefreshLeaseCommand(), cancellationToken).ConfigureAwait(false);
        }

        HeartbeatResult heartbeat = await dispatcher
            .SendAsync(new SendHeartbeatCommand(), cancellationToken)
            .ConfigureAwait(false);

        return (heartbeat.Level is not Domain.Licensing.EnforcementLevel.Normal, now);
    }
}

/// <summary>
/// Rolls up yesterday's counts and delivers whatever is queued (<c>LICENSING.md</c> §9).
/// </summary>
/// <remarks>
/// <para>
/// Runs hourly rather than daily, and rolls up the previous day each time. That sounds wasteful and is
/// not: the rollup is idempotent by <c>(node, period)</c>, and a store that was switched off at
/// midnight — which is most of them — would otherwise miss its own day entirely.
/// </para>
/// <para>
/// A read-only tenant is skipped quietly. Metering is what a <em>trading</em> tenant is billed on, and
/// a background pass that threw a refusal every hour would fill a shop's log with a refusal nobody can
/// act on.
/// </para>
/// </remarks>
/// <param name="services">The root container; each pass takes its own scope.</param>
/// <param name="host">The tenant a pass runs as.</param>
/// <param name="logger">Where a failed pass is recorded.</param>
public sealed class MeteringRollupService(
    IServiceProvider services,
    LicensingHostTenant host,
    ILogger<MeteringRollupService> logger) : BackgroundService
{
    /// <summary>How often the rollup and delivery pass runs.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = services.CreateScope();

                ITenantContext tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                tenant.SetTenant(host.TenantId, host.StoreId);

                IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

                await dispatcher.SendAsync(new RollUpMeteringCommand(), stoppingToken).ConfigureAwait(false);
                await dispatcher.SendAsync(new DeliverMeteringCommand(), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (LicenceReadOnlyException)
            {
                // Expected while a tenant is restricted, and not worth a line. The queue waits; when
                // they pay, every missed day goes out exactly once.
            }
#pragma warning disable CA1031 // A background pass must survive anything a pass can throw.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                logger.LogWarning(failure, "Metering pass failed; retrying at the next interval.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
