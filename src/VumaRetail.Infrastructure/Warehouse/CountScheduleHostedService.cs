using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Warehouse;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Infrastructure.Warehouse;

/// <summary>Advances due count schedules without creating duplicate runs.</summary>
public sealed class CountScheduleHostedService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<CountScheduleHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await AdvanceDueSchedulesAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task AdvanceDueSchedulesAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ICountScheduleRepository schedules = scope.ServiceProvider.GetRequiredService<ICountScheduleRepository>();
        DateTimeOffset now = clock.UtcNow;
        IReadOnlyList<CountSchedule> due = await schedules.ListActiveDueAsync(now, cancellationToken)
            .ConfigureAwait(false);

        foreach (CountSchedule schedule in due)
        {
            schedule.Advance(NextRun(schedule, now));
            logger.LogInformation("Advanced count schedule {ScheduleId} to {NextRunAt}.", schedule.Id, schedule.NextRunAt);
        }
    }

    private static DateTimeOffset NextRun(CountSchedule schedule, DateTimeOffset now) => schedule.Cadence switch
    {
        CountCadence.Daily => now.AddDays(1),
        CountCadence.Weekly => now.AddDays(7),
        CountCadence.Monthly => now.AddMonths(1),
        _ => now.AddDays(1)
    };
}
