using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions.Registry;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>
/// Default implementation of <see cref="IAlarmService"/> that logs alarms and raises
/// through the host's logging infrastructure (ADR-105).
/// </summary>
public sealed class AlarmService : IAlarmService
{
    private readonly ILogger<AlarmService> _logger;

    public AlarmService(ILogger<AlarmService> logger)
    {
        _logger = logger;
    }

    public Task RaiseAlarmAsync(Guid tenantId, string alarmType, string message, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "ALARM [{AlarmType}] Tenant {TenantId}: {Message}", alarmType, tenantId, message);
        return Task.CompletedTask;
    }
}
