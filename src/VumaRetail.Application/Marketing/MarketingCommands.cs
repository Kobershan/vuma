#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Marketing;

namespace VumaRetail.Application.Marketing;

[CommandSideEffect(SideEffect.Write)]
public sealed record CreateMarketingCampaignCommand(Guid CompanyId, Guid? StoreId, string Name, string TemplateId, DateTimeOffset ScheduledAt) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record ScheduleMarketingCampaignCommand(Guid CampaignId) : ICommand;
[CommandSideEffect(SideEffect.Write)]
public sealed record CancelMarketingCampaignCommand(Guid CampaignId) : ICommand;
[CommandSideEffect(SideEffect.Write)]
public sealed record QueueOutboundMessageCommand(Guid CompanyId, Guid? StoreId, Guid CampaignId, Guid CustomerId, string IdempotencyKey, DateTimeOffset ScheduledAt) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record SuppressOutboundMessageCommand(Guid MessageId) : ICommand;
[CommandSideEffect(SideEffect.Write)]
public sealed record MarkOutboundMessageSentCommand(Guid MessageId) : ICommand;

public sealed class CreateMarketingCampaignCommandHandler(IMarketingCampaignRepository campaigns, ITenantContext tenant, ICompanyContext company) : ICommandHandler<CreateMarketingCampaignCommand, Guid>
{
    public Task<Guid> HandleAsync(CreateMarketingCampaignCommand c, CancellationToken token = default)
    { ArgumentNullException.ThrowIfNull(c); EnsureCompany(company, c.CompanyId); var campaign = MarketingCampaign.Create(tenant.TenantId, c.StoreId, c.CompanyId, c.Name, c.TemplateId, c.ScheduledAt); campaigns.Add(campaign); return Task.FromResult(campaign.Id); }
    internal static void EnsureCompany(ICompanyContext context, Guid expected)
    {
        if (context.CompanyId is not { } active || active != expected)
        {
            throw new InvalidOperationException("The marketing company is not the active company.");
        }
    }
}
public sealed class ScheduleMarketingCampaignCommandHandler(IMarketingCampaignRepository campaigns, ICompanyContext company, IClock clock) : ICommandHandler<ScheduleMarketingCampaignCommand, Unit>
{ public async Task<Unit> HandleAsync(ScheduleMarketingCampaignCommand c, CancellationToken token = default) { ArgumentNullException.ThrowIfNull(c); var campaign = await campaigns.FindAsync(c.CampaignId, token).ConfigureAwait(false) ?? throw new KeyNotFoundException("Marketing campaign was not found."); CreateMarketingCampaignCommandHandler.EnsureCompany(company, campaign.CompanyId!.Value); campaign.Schedule(clock.UtcNow); return Unit.Value; } }
public sealed class CancelMarketingCampaignCommandHandler(IMarketingCampaignRepository campaigns, ICompanyContext company) : ICommandHandler<CancelMarketingCampaignCommand, Unit>
{ public async Task<Unit> HandleAsync(CancelMarketingCampaignCommand c, CancellationToken token = default) { ArgumentNullException.ThrowIfNull(c); var campaign = await campaigns.FindAsync(c.CampaignId, token).ConfigureAwait(false) ?? throw new KeyNotFoundException("Marketing campaign was not found."); CreateMarketingCampaignCommandHandler.EnsureCompany(company, campaign.CompanyId!.Value); campaign.Cancel(); return Unit.Value; } }
public sealed class QueueOutboundMessageCommandHandler(IOutboundMessageRepository messages, ITenantContext tenant, ICompanyContext company) : ICommandHandler<QueueOutboundMessageCommand, Guid>
{
    public async Task<Guid> HandleAsync(QueueOutboundMessageCommand c, CancellationToken token = default)
    { ArgumentNullException.ThrowIfNull(c); CreateMarketingCampaignCommandHandler.EnsureCompany(company, c.CompanyId); var existing = await messages.FindByIdempotencyKeyAsync(c.IdempotencyKey.Trim(), token).ConfigureAwait(false); if (existing is not null) { return existing.Id; } var message = OutboundMessage.Queue(tenant.TenantId, c.StoreId, c.CompanyId, c.CampaignId, c.CustomerId, c.IdempotencyKey, c.ScheduledAt); messages.Add(message); return message.Id; }
}
public sealed class SuppressOutboundMessageCommandHandler(IOutboundMessageRepository messages) : ICommandHandler<SuppressOutboundMessageCommand, Unit>
{ public async Task<Unit> HandleAsync(SuppressOutboundMessageCommand c, CancellationToken token = default) { ArgumentNullException.ThrowIfNull(c); var message = await messages.FindAsync(c.MessageId, token).ConfigureAwait(false) ?? throw new KeyNotFoundException("Outbound message was not found."); message.Suppress(); return Unit.Value; } }
public sealed class MarkOutboundMessageSentCommandHandler(IOutboundMessageRepository messages) : ICommandHandler<MarkOutboundMessageSentCommand, Unit>
{ public async Task<Unit> HandleAsync(MarkOutboundMessageSentCommand c, CancellationToken token = default) { ArgumentNullException.ThrowIfNull(c); var message = await messages.FindAsync(c.MessageId, token).ConfigureAwait(false) ?? throw new KeyNotFoundException("Outbound message was not found."); message.MarkSent(); return Unit.Value; } }
