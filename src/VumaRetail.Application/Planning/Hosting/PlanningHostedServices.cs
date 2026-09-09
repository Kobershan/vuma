#pragma warning disable CS1591
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Planning.Commands;

namespace VumaRetail.Application.Planning.Hosting;

public sealed record PlanningHostTenant(Guid TenantId, Guid? StoreId);

public abstract class PlanningHostedService(
    IServiceProvider services,
    PlanningHostTenant host,
    ILogger logger) : BackgroundService
{
    protected abstract TimeSpan Interval { get; }

    protected abstract string PassName { get; }

    protected abstract Task RunPassAsync(IDispatcher dispatcher, CancellationToken cancellationToken);

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
                await RunPassAsync(dispatcher, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031
            catch (Exception failure)
#pragma warning restore CA1031
            {
                logger.LogWarning(failure, "{Pass} pass failed; retrying at the next interval.", PassName);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}

/// <summary>Nightly demand-history rollup.</summary>
public sealed class DemandHistoryRollupHostedService(
    IServiceProvider services,
    PlanningHostTenant host,
    ILogger<DemandHistoryRollupHostedService> logger)
    : PlanningHostedService(services, host, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(24);

    protected override string PassName => "Demand-history rollup";

    protected override async Task RunPassAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        RollupDemandHistoryOutcome outcome = await dispatcher
            .SendAsync(new RollupDemandHistoryCommand(), cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Demand-history rollup: {Rows} rows across {Skus} SKU/locations.", outcome.RowsUpserted, outcome.Skus);
    }
}

/// <summary>Weekly forecast run (Sundays).</summary>
public sealed class ForecastRunHostedService(
    IServiceProvider services,
    PlanningHostTenant host,
    ILogger<ForecastRunHostedService> logger)
    : PlanningHostedService(services, host, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromDays(7);

    protected override string PassName => "Forecast run";

    protected override async Task RunPassAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        GenerateForecastsOutcome outcome = await dispatcher
            .SendAsync(new GenerateForecastsCommand(), cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Forecast run: {Forecasts} snapshots across {Skus} SKU/locations.", outcome.ForecastsWritten, outcome.Skus);

        await dispatcher.SendAsync(new RefreshSafetyStockCommand(), cancellationToken).ConfigureAwait(false);
        await dispatcher.SendAsync(new RunClassificationCommand(), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Daily replenishment run, including the backorder reattempt hook.</summary>
public sealed class ReplenishmentRunHostedService(
    IServiceProvider services,
    PlanningHostTenant host,
    ILogger<ReplenishmentRunHostedService> logger)
    : PlanningHostedService(services, host, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(24);

    protected override string PassName => "Replenishment run";

    protected override async Task RunPassAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        RunReplenishmentOutcome outcome = await dispatcher
            .SendAsync(new RunReplenishmentCommand(), cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Replenishment run: {Raised} raised, {Expired} expired, {Skipped} skipped, {Backorders} backorder lines reallocated.",
            outcome.SuggestionsRaised, outcome.Expired, outcome.Skipped, outcome.BackordersReallocated);
    }
}

/// <summary>Daily markdown activation sweep — missed dates activate on the next run.</summary>
public sealed class MarkdownActivationHostedService(
    IServiceProvider services,
    PlanningHostTenant host,
    ILogger<MarkdownActivationHostedService> logger)
    : PlanningHostedService(services, host, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(24);

    protected override string PassName => "Markdown activation sweep";

    protected override async Task RunPassAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        SweepMarkdownActivationsOutcome outcome = await dispatcher
            .SendAsync(new SweepMarkdownActivationsCommand(), cancellationToken)
            .ConfigureAwait(false);

        if (outcome.PlansActivated > 0)
        {
            logger.LogInformation(
                "Markdown sweep: {Plans} plans activated ({Promotions} promotions).",
                outcome.PlansActivated, outcome.PromotionsCreated);
        }
    }
}


