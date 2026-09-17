#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Marketing;

namespace VumaRetail.Application.Marketing;

public sealed record OutboundMessageResult(Guid Id, Guid CompanyId, Guid? CampaignId, Guid CustomerId,
    DateTimeOffset ScheduledAt, string Channel, string Classification, string Status, string? ProviderEventId);

public sealed record ListDeliveriesQuery(Guid CompanyId, int Limit = 100) : IQuery<IReadOnlyList<OutboundMessageResult>>;
public sealed record ListSuppressionsQuery(Guid CompanyId, int Limit = 100) : IQuery<IReadOnlyList<OutboundMessageResult>>;

public sealed class ListDeliveriesQueryHandler(IOutboundMessageRepository messages, ICompanyContext company)
    : IQueryHandler<ListDeliveriesQuery, IReadOnlyList<OutboundMessageResult>>
{
    private static readonly OutboundMessageStatus[] DeliveryStatuses = [OutboundMessageStatus.Sent, OutboundMessageStatus.Failed];

    public async Task<IReadOnlyList<OutboundMessageResult>> HandleAsync(ListDeliveriesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        CreateMarketingCampaignCommandHandler.EnsureCompany(company, query.CompanyId);
        IReadOnlyList<OutboundMessage> found = await messages.ListByStatusAsync(
            query.CompanyId, DeliveryStatuses, query.Limit, cancellationToken).ConfigureAwait(false);
        return found.Select(ToResult).ToList();
    }

    internal static OutboundMessageResult ToResult(OutboundMessage message) => new(message.Id, message.CompanyId!.Value,
        message.CampaignId, message.CustomerId, message.ScheduledAt, message.Channel.ToString(),
        message.Classification.ToString(), message.Status.ToString(), message.ProviderEventId);
}

public sealed class ListSuppressionsQueryHandler(IOutboundMessageRepository messages, ICompanyContext company)
    : IQueryHandler<ListSuppressionsQuery, IReadOnlyList<OutboundMessageResult>>
{
    private static readonly OutboundMessageStatus[] SuppressionStatuses = [OutboundMessageStatus.Suppressed];

    public async Task<IReadOnlyList<OutboundMessageResult>> HandleAsync(ListSuppressionsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        CreateMarketingCampaignCommandHandler.EnsureCompany(company, query.CompanyId);
        IReadOnlyList<OutboundMessage> found = await messages.ListByStatusAsync(
            query.CompanyId, SuppressionStatuses, query.Limit, cancellationToken).ConfigureAwait(false);
        return found.Select(ListDeliveriesQueryHandler.ToResult).ToList();
    }
}
