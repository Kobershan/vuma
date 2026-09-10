#pragma warning disable CS1591
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Loyalty.Commands;
using VumaRetail.Application.Loyalty.Queries;

namespace VumaRetail.Application.Loyalty.Hosting;

public sealed record LoyaltyHostTenant(Guid TenantId, Guid? StoreId);

public abstract class LoyaltyHostedService(
    IServiceProvider services,
    LoyaltyHostTenant host,
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

/// <summary>
/// Retries queued loyalty transactions. Each transaction retries under its original idempotency
/// key in its own command (own pipeline transaction), so one poisoned row never blocks the rest.
/// </summary>
public sealed class LoyaltyRetryHostedService(
    IServiceProvider services,
    LoyaltyHostTenant host,
    ILogger<LoyaltyRetryHostedService> logger)
    : LoyaltyHostedService(services, host, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

    protected override string PassName => "Loyalty retry";

    protected override async Task RunPassAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> queued = await dispatcher
            .QueryAsync(new ListQueuedTransactionsQuery(50), cancellationToken)
            .ConfigureAwait(false);

        int confirmed = 0;
        foreach (Guid transactionId in queued)
        {
            RetryDisposition disposition = await dispatcher
                .SendAsync(new RetryLoyaltyTransactionCommand(transactionId), cancellationToken)
                .ConfigureAwait(false);

            if (disposition == RetryDisposition.Confirmed)
            {
                confirmed++;
            }
        }

        logger.LogInformation(
            "Loyalty retry: {Confirmed} of {Queued} queued transactions confirmed.", confirmed, queued.Count);
    }
}
