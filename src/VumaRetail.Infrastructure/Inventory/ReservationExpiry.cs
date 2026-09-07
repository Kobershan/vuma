using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Inventory.Commands;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Inventory;

/// <summary>Reads the tenant's reservation expiry policy from the registry, with stage defaults.</summary>
/// <param name="registry">The registry database.</param>
/// <remarks>
/// Read at hold time: the commit stamps <c>ExpiresAt</c> from this, and the expiry job only reads
/// the stamp — so a policy change never rewrites live holds. Defaults: order and pro-forma
/// approval holds 72 hours; transfer and shipment holds never expire.
/// </remarks>
public sealed class RegistryReservationExpiryPolicy(VumaRegistryDbContext registry) : IReservationExpiryPolicy
{
    /// <inheritdoc />
    public async Task<TimeSpan?> ResolveAsync(ReservationSource source, CancellationToken cancellationToken = default)
    {
        ReservationExpiryPolicyRow? row = await registry.ReservationExpiryPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(policy => policy.Source == source.ToString(), cancellationToken)
            .ConfigureAwait(false);

        if (row is not null)
        {
            return row.ExpiryHours is { } hours ? TimeSpan.FromHours(hours) : null;
        }

        return source switch
        {
            ReservationSource.Order => TimeSpan.FromHours(72),
            ReservationSource.ProFormaApproval => TimeSpan.FromHours(72),
            ReservationSource.Transfer => null,
            ReservationSource.Shipment => null,
            _ => TimeSpan.FromHours(72),
        };
    }
}

/// <summary>Sweeps due reservation holds into expiry, one company at a time.</summary>
/// <param name="services">The root container; each company gets its own scope.</param>
/// <param name="host">The tenant this host serves.</param>
/// <param name="options">How often the sweep runs.</param>
/// <param name="clock">The only source of time.</param>
/// <param name="logger">Where a failed company pass is recorded.</param>
/// <remarks>
/// Same shape as Stage 07's <c>FinanceReconciliationHostedService</c>: per pass, list the
/// registry's active companies, then bind each in its own child scope and expire through the full
/// command pipeline. One company per scope, one scope per transaction — a failing company never
/// blocks the others, and failures are per-company results in the fan-out spirit.
/// </remarks>
public sealed class ReservationExpiryHostedService(
    IServiceProvider services,
    InventoryHostTenant host,
    IOptions<ReservationExpiryOptions> options,
    IClock clock,
    ILogger<ReservationExpiryHostedService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await SweepOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "Reservation expiry sweep failed; the next pass retries.");
            }
        }
    }

    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        List<Guid> companies;
        using (IServiceScope scope = services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(host.TenantId, host.StoreId);

            VumaRegistryDbContext registry = scope.ServiceProvider.GetRequiredService<VumaRegistryDbContext>();
            companies = await registry.Companies
                .AsNoTracking()
                .Where(company => company.TenantId == host.TenantId
                    && company.LifecycleState == CompanyLifecycleState.Active)
                .Select(company => company.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (Guid companyId in companies)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                using IServiceScope scope = services.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(host.TenantId, host.StoreId);
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);

                int expired = await scope.ServiceProvider
                    .GetRequiredService<IDispatcher>()
                    .SendAsync(new ExpireCompanyReservationsCommand(companyId), cancellationToken)
                    .ConfigureAwait(false);

                if (expired > 0)
                {
                    logger.LogInformation(
                        "Expired {Expired} reservation holds in company {CompanyId}.",
                        expired,
                        companyId);
                }
            }
            catch (Exception failure)
            {
                logger.LogError(
                    failure,
                    "Reservation expiry failed for company {CompanyId} at {Now}; retrying next pass.",
                    companyId,
                    clock.UtcNow);
            }
        }
    }
}
