using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Finance.Commands;

namespace VumaRetail.Finance.Hosting;

/// <summary>
/// The tenant a Finance background pass runs as, and the store it runs for.
/// </summary>
/// <remarks>
/// Same shape as Stage 04b's <c>LicensingHostTenant</c> (ADR-048): a hosted service has no request
/// and therefore no ambient tenant, so the host says which tenant its background work belongs to.
/// </remarks>
/// <param name="TenantId">The tenant this host serves.</param>
/// <param name="StoreId">The store this host runs, where it runs one.</param>
public sealed record FinanceHostTenant(Guid TenantId, Guid? StoreId);

/// <summary>
/// Runs ADR-016's "automated daily job": the control-account variance check, across every open
/// period, once every 24 hours (ADR-063).
/// </summary>
/// <remarks>
/// Issues <see cref="RunDailyReconciliationCheckCommand"/> through the full command pipeline —
/// validation, transaction, audit — exactly as a person clicking a "check now" button would, so
/// there is one code path for the check whether it is scheduled or requested on demand.
/// </remarks>
/// <param name="services">The root container; each pass takes its own scope.</param>
/// <param name="host">The tenant a pass runs as.</param>
/// <param name="logger">Where a failed pass is recorded.</param>
public sealed class FinanceReconciliationHostedService(
    IServiceProvider services, FinanceHostTenant host, ILogger<FinanceReconciliationHostedService> logger)
    : BackgroundService
{
    /// <summary>How often the check runs.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

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

                await dispatcher.SendAsync(new RunDailyReconciliationCheckCommand(), stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A background pass must survive anything a pass can throw.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                logger.LogWarning(failure, "Finance reconciliation pass failed; retrying at the next interval.");
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
