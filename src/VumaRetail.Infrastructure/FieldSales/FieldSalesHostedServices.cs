using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.FieldSales.Commands;
using VumaRetail.Infrastructure.Persistence;

using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.FieldSales.Commands;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.FieldSales;

/// <summary>The installation tenant the field-sales passes run as.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="StoreId">The store, if the installation names one.</param>
public sealed record FieldSalesHostTenant(Guid TenantId, Guid? StoreId);

/// <summary>Lapses undecided pro formas past their expiry, daily (Stage 14b).</summary>
public sealed class ProFormaExpiryHostedService(
    IServiceProvider services,
    FieldSalesHostTenant host,
    ILogger<ProFormaExpiryHostedService> logger,
    IClock clock) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // One company at a time: pro formas live in company databases (ADR-148), so the
                // pass fans out per active company exactly like the reservation expiry sweep.
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
                        .ToListAsync(stoppingToken)
                        .ConfigureAwait(false);
                }

                int expired = 0;
                foreach (Guid companyId in companies)
                {
                    try
                    {
                        using IServiceScope scope = services.CreateScope();
                        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(host.TenantId, host.StoreId);
                        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);
                        IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
                        expired += await dispatcher
                            .SendAsync(new ExpireProFormasCommand(), stoppingToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception failure)
                    {
                        logger.LogWarning(failure, "Pro forma expiry pass failed for company {CompanyId}.", companyId);
                    }
                }

                if (expired > 0)
                {
                    logger.LogInformation("Pro forma expiry pass: {Expired} lapsed.", expired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031
            catch (Exception failure)
#pragma warning restore CA1031
            {
                logger.LogWarning(failure, "Pro forma expiry pass failed; retrying at the next interval.");
            }

            try
            {
                DateTimeOffset now = clock.UtcNow;
                DateTimeOffset next = now.Date.AddDays(1).AddHours(2);
                await Task.Delay(next - now, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}

/// <summary>Snapshots the last closed month per company, monthly (Stage 14b, ADR-110).</summary>
public sealed class RepPerformanceSnapshotHostedService(
    IServiceProvider services,
    FieldSalesHostTenant host,
    ILogger<RepPerformanceSnapshotHostedService> logger,
    IClock clock) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
                DateOnly lastClosed = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);

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
                        .ToListAsync(stoppingToken)
                        .ConfigureAwait(false);
                }

                foreach (Guid companyId in companies)
                {
                    try
                    {
                        using IServiceScope scope = services.CreateScope();
                        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(host.TenantId, host.StoreId);
                        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);
                        IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
                        int snapshotted = await dispatcher
                            .SendAsync(new SnapshotPerformanceCommand(lastClosed), stoppingToken)
                            .ConfigureAwait(false);
                        logger.LogInformation(
                            "Performance snapshot pass: {Count} snapshots for {Period:yyyy-MM}.", snapshotted, lastClosed);
                    }
                    catch (Exception failure)
                    {
                        logger.LogWarning(failure, "Performance snapshot pass failed for company {CompanyId}.", companyId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031
            catch (Exception failure)
#pragma warning restore CA1031
            {
                logger.LogWarning(failure, "Performance snapshot pass failed; retrying at the next interval.");
            }

            try
            {
                DateTimeOffset now = clock.UtcNow;
                DateTimeOffset next = new DateTimeOffset(
                    new DateOnly(now.Year, now.Month, 1).AddMonths(1).ToDateTime(TimeOnly.MinValue),
                    now.Offset).AddHours(3);
                await Task.Delay(next - now, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
