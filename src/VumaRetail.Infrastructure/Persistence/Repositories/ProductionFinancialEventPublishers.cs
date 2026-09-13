using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Manufacturing;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>Posts production scrap/WIP facts through configured finance rules.</summary>
public sealed class FinancialProductionAccountingEventPublisher(
    IFinancialEventPoster poster,
    ILogger<FinancialProductionAccountingEventPublisher> logger) : IProductionAccountingEventPublisher
{
    /// <inheritdoc />
    public async Task PublishScrapAsync(ProductionScrapAccountingEvent accountingEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountingEvent);
        try
        {
            await poster.PostAsync(new FinancialEvent(
                "manufacturing.scrap.recorded",
                accountingEvent.TenantId,
                null,
                accountingEvent.OccurredAt,
                accountingEvent.OperationId.ToString(),
                new Dictionary<string, Money>(StringComparer.Ordinal) { ["Value"] = accountingEvent.Value }), cancellationToken).ConfigureAwait(false);
        }
        catch (PostingRuleNotFoundException)
        {
            logger.LogWarning("No posting rule for manufacturing.scrap.recorded; scrap operation {OperationId} remains recorded without a journal.", accountingEvent.OperationId);
        }
    }
}

/// <summary>Records production accounting facts when finance is not registered.</summary>
public sealed class LoggingProductionAccountingEventPublisher(ILogger<LoggingProductionAccountingEventPublisher> logger) : IProductionAccountingEventPublisher
{
    /// <inheritdoc />
    public Task PublishScrapAsync(ProductionScrapAccountingEvent accountingEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountingEvent);
        logger.LogDebug("Production scrap operation {OperationId} recorded for order {ProductionOrderId}; finance is not registered.", accountingEvent.OperationId, accountingEvent.ProductionOrderId);
        return Task.CompletedTask;
    }
}
