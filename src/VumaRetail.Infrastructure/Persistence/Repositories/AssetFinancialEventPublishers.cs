using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Assets;
using VumaRetail.Domain.Assets;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

public sealed class FinancialAssetDepreciationEventPublisher(
    IFinancialEventPoster poster,
    ILogger<FinancialAssetDepreciationEventPublisher> logger) : IAssetDepreciationFinancialEventPublisher
{
    public const string DepreciationRecordedEventType = "assets.depreciation.recorded";
    public const string DepreciationAmountKey = "Depreciation";

    public async Task PublishAsync(DepreciationRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        DateOnly period = run.Period;
        FinancialEvent financialEvent = new(
            DepreciationRecordedEventType,
            run.TenantId,
            run.StoreId,
            new DateTimeOffset(period.Year, period.Month, DateTime.DaysInMonth(period.Year, period.Month),
                23, 59, 59, TimeSpan.Zero),
            run.Id.ToString(),
            new Dictionary<string, Money>(StringComparer.Ordinal) { [DepreciationAmountKey] = run.Amount });

        try
        {
            await poster.PostAsync(financialEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (PostingRuleNotFoundException)
        {
            logger.LogWarning(
                "No posting rule for {EventType}; depreciation run {DepreciationRunId} was recorded but raised no journal.",
                DepreciationRecordedEventType, run.Id);
        }
    }
}

public sealed class LoggingAssetDepreciationEventPublisher(
    ILogger<LoggingAssetDepreciationEventPublisher> logger) : IAssetDepreciationFinancialEventPublisher
{
    public Task PublishAsync(DepreciationRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        logger.LogDebug("Depreciation run {DepreciationRunId} recorded for {Amount}; no financial poster is registered.",
            run.Id, run.Amount);
        return Task.CompletedTask;
    }
}
