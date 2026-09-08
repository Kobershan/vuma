#pragma warning disable CS1591
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.CustomerAccounts.Commands.Scheduled;

namespace VumaRetail.Application.CustomerAccounts.Hosting;

public sealed record CustomerAccountsHostTenant(Guid TenantId, Guid? StoreId);

public sealed class LayByExpiryHostedService(
    IServiceProvider services,
    CustomerAccountsHostTenant host,
    ILogger<LayByExpiryHostedService> logger,
    IClock clock) : BackgroundService
{
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
                ExpiryOutcome outcome = await dispatcher
                    .SendAsync(new ExpireLayByAgreementsCommand(), stoppingToken)
                    .ConfigureAwait(false);

                if (outcome.Expired > 0 || outcome.Reminded > 0)
                {
                    logger.LogInformation(
                        "Lay-by expiry pass: {Expired} expired, {Reminded} reminded.", outcome.Expired, outcome.Reminded);
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
                logger.LogWarning(failure, "Lay-by expiry pass failed; retrying at the next interval.");
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

public sealed class AccountInterestHostedService(
    IServiceProvider services,
    CustomerAccountsHostTenant host,
    ILogger<AccountInterestHostedService> logger,
    IClock clock) : BackgroundService
{
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
                int raised = await dispatcher
                    .SendAsync(new AccrueAccountInterestCommand(), stoppingToken)
                    .ConfigureAwait(false);

                if (raised > 0)
                {
                    logger.LogInformation("Interest pass raised {Count} interest invoices.", raised);
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
                logger.LogWarning(failure, "Interest pass failed; retrying at the next interval.");
            }

            try
            {
                DateTimeOffset now = clock.UtcNow;
                var first = new DateTimeOffset(now.Year, now.Month, 1, 2, 0, 0, TimeSpan.Zero).AddMonths(1);
                await Task.Delay(first - now, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
