using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Reporting;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Reporting;

/// <summary>Configuration for the durable report scheduling and export sweep.</summary>
public sealed class ReportSchedulingOptions
{
    public const string SectionName = "Vuma:Reporting:Scheduling";
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
    public int BatchSize { get; set; } = 100;
}

/// <summary>The installation tenant used by the StoreServer reporting sweep.</summary>
public sealed record ReportingHostTenant(Guid TenantId, Guid? StoreId);

/// <summary>
/// Enqueues due report schedules and executes queued exports for every active company. Each
/// company receives a fresh scope so its tenant/company context and DbContext cannot leak.
/// </summary>
public sealed class ReportSchedulingHostedService(
    IServiceProvider services,
    ReportingHostTenant host,
    IOptions<ReportSchedulingOptions> options,
    ILogger<ReportSchedulingHostedService> logger) : BackgroundService
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
                await ProcessAllCompaniesAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "Report scheduling sweep failed; the next pass will retry.");
            }
        }
    }

    private async Task ProcessAllCompaniesAsync(CancellationToken cancellationToken)
    {
        List<Guid> companies;
        using (IServiceScope scope = services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(host.TenantId, host.StoreId);
            VumaRegistryDbContext registry = scope.ServiceProvider.GetRequiredService<VumaRegistryDbContext>();
            companies = await registry.Companies.AsNoTracking()
                .Where(company => company.TenantId == host.TenantId && company.LifecycleState == CompanyLifecycleState.Active)
                .Select(company => company.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
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
                IServiceProvider provider = scope.ServiceProvider;
                provider.GetRequiredService<ITenantContext>().SetTenant(host.TenantId, host.StoreId);
                provider.GetRequiredService<ICompanyContext>().SetCompany(companyId);
                IClock clock = provider.GetRequiredService<IClock>();
                IUnitOfWork unitOfWork = provider.GetRequiredService<IUnitOfWork>();
                IReportScheduleRunner runner = provider.GetRequiredService<IReportScheduleRunner>();
                int batchSize = Math.Clamp(options.Value.BatchSize, 1, 200);

                ReportScheduleRunResult result = await runner.EnqueueDueAsync(clock.UtcNow, batchSize, cancellationToken).ConfigureAwait(false);
                if (result.Examined > 0)
                {
                    await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
                }

                IReadOnlyList<Domain.Reporting.ReportExport> queued = await provider
                    .GetRequiredService<IReportingRepository>()
                    .ListQueuedExportsAsync(companyId, batchSize, cancellationToken).ConfigureAwait(false);
                ReportExportExecutor executor = provider.GetRequiredService<ReportExportExecutor>();
                foreach (Domain.Reporting.ReportExport export in queued)
                {
                    try
                    {
                        if (await executor.ExecuteAsync(export.Id, cancellationToken).ConfigureAwait(false))
                        {
                            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception failure)
                    {
                        logger.LogError(failure, "Report export {ExportId} failed for company {CompanyId}; continuing with the batch.", export.Id, companyId);
                        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                if (result.Enqueued > 0 || queued.Count > 0)
                {
                    logger.LogInformation("Report scheduling sweep processed {Enqueued} schedules and {Exports} exports for company {CompanyId}.", result.Enqueued, queued.Count, companyId);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "Report scheduling failed for company {CompanyId}; continuing with other companies.", companyId);
            }
        }
    }
}
