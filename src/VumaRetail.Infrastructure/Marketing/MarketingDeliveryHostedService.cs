using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Marketing;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Marketing;

/// <summary>Configuration for the durable marketing delivery sweep.</summary>
public sealed class MarketingDeliveryOptions
{
    public const string SectionName = "Vuma:Marketing:Delivery";
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
    public int BatchSize { get; set; } = 100;
}

/// <summary>The installation tenant used by the store-server marketing sweep.</summary>
public sealed record MarketingHostTenant(Guid TenantId, Guid? StoreId);

/// <summary>
/// Dispatches due marketing messages for every active company without creating a second delivery
/// path. Each company gets a fresh scope and therefore one company database context.
/// </summary>
public sealed class MarketingDeliveryHostedService(
    IServiceProvider services,
    MarketingHostTenant host,
    IOptions<MarketingDeliveryOptions> options,
    ILogger<MarketingDeliveryHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = options.Value.Interval <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(1)
            : options.Value.Interval;
        using PeriodicTimer timer = new(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await DispatchAllCompaniesAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "Marketing delivery sweep failed; the next pass will retry.");
            }
        }
    }

    private async Task DispatchAllCompaniesAsync(CancellationToken cancellationToken)
    {
        List<Guid> companies;
        using (IServiceScope scope = services.CreateScope())
        {
            ITenantContext tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.SetTenant(host.TenantId, host.StoreId);
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
                int processed = await scope.ServiceProvider
                    .GetRequiredService<IMarketingDeliveryWorker>()
                    .DispatchDueAsync(companyId, options.Value.BatchSize, cancellationToken)
                    .ConfigureAwait(false);
                if (processed > 0)
                {
                    logger.LogInformation("Marketing delivery sweep processed {Count} messages for company {CompanyId}.", processed, companyId);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "Marketing delivery failed for company {CompanyId}; continuing with other companies.", companyId);
            }
        }
    }
}
