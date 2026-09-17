#pragma warning disable CS1591
using VumaRetail.Domain.Marketing;

namespace VumaRetail.Application.Marketing;

public interface IMarketingCampaignRepository
{
    Task<MarketingCampaign?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(MarketingCampaign campaign);
}

public interface IOutboundMessageRepository
{
    Task<OutboundMessage?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OutboundMessage?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<OutboundMessage?> FindByProviderEventIdAsync(Guid companyId, string providerEventId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboundMessage>> ListQueuedAsync(Guid companyId, DateTimeOffset asAt, bool dueOnly,
        int limit, CancellationToken cancellationToken = default);
    void Add(OutboundMessage message);
}

public sealed record MarketingTransportResult(string ProviderEventId, string PayloadFingerprint, bool Delivered);

public interface IMarketingTransport
{
    Task<MarketingTransportResult> SendAsync(OutboundMessage message, MarketingCampaign campaign,
        CancellationToken cancellationToken = default);
}

/// <summary>Runs due marketing messages through the durable delivery boundary.</summary>
public interface IMarketingDeliveryWorker
{
    Task<int> DispatchDueAsync(Guid companyId, int limit = 100, CancellationToken cancellationToken = default);
}

public interface IJourneyDefinitionRepository
{
    Task<JourneyDefinition?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(JourneyDefinition journey);
}

public interface IJourneyEnrollmentRepository
{
    Task<JourneyEnrollment?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default);
    void Add(JourneyEnrollment enrollment);
}

public interface IAttributionEventRepository
{
    void Add(AttributionEvent attributionEvent);
    Task<IReadOnlyList<AttributionEvent>> ListAsync(Guid companyId, Guid? campaignId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
