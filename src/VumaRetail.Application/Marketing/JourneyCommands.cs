#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Marketing;

namespace VumaRetail.Application.Marketing;

[CommandSideEffect(SideEffect.Write)]
public sealed record CreateJourneyDefinitionCommand(Guid CompanyId, string Name, int Version, string DefinitionJson) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record PublishJourneyDefinitionCommand(Guid JourneyId) : ICommand;
[CommandSideEffect(SideEffect.Write)]
public sealed record EnrollJourneyCommand(Guid CompanyId, Guid JourneyId, Guid CustomerId, string IdempotencyKey, DateTimeOffset NextRunAt) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record RecordAttributionEventCommand(Guid CompanyId, Guid? CampaignId, Guid? MessageId, Guid CustomerId, string EventType) : ICommand<Guid>;

public sealed class CreateJourneyDefinitionCommandHandler(IJourneyDefinitionRepository journeys, ITenantContext tenant, ICompanyContext company) : ICommandHandler<CreateJourneyDefinitionCommand, Guid>
{
    public Task<Guid> HandleAsync(CreateJourneyDefinitionCommand c, CancellationToken ct = default)
    { if (company.CompanyId != c.CompanyId) throw new InvalidOperationException("The marketing company is not active."); var j = JourneyDefinition.Create(tenant.TenantId, c.CompanyId, c.Name, c.Version, c.DefinitionJson); journeys.Add(j); return Task.FromResult(j.Id); }
}
public sealed class PublishJourneyDefinitionCommandHandler(IJourneyDefinitionRepository journeys, ICompanyContext company) : ICommandHandler<PublishJourneyDefinitionCommand, Unit>
{
    public async Task<Unit> HandleAsync(PublishJourneyDefinitionCommand c, CancellationToken ct = default)
    { var j = await journeys.FindAsync(c.JourneyId, ct) ?? throw new KeyNotFoundException("Journey was not found."); if (j.CompanyId != company.CompanyId) throw new InvalidOperationException("The marketing company is not active."); j.Publish(); return Unit.Value; }
}
public sealed class EnrollJourneyCommandHandler(IJourneyDefinitionRepository journeys, IJourneyEnrollmentRepository enrollments, ITenantContext tenant, ICompanyContext company) : ICommandHandler<EnrollJourneyCommand, Guid>
{
    public async Task<Guid> HandleAsync(EnrollJourneyCommand c, CancellationToken ct = default)
    { if (company.CompanyId != c.CompanyId) throw new InvalidOperationException("The marketing company is not active."); var existing = await enrollments.FindByIdempotencyKeyAsync(c.IdempotencyKey, ct); if (existing is not null) return existing.Id; var j = await journeys.FindAsync(c.JourneyId, ct) ?? throw new KeyNotFoundException("Journey was not found."); if (j.CompanyId != c.CompanyId || j.Status != JourneyDefinitionStatus.Published) throw new InvalidOperationException("Only a published journey in the active company can accept enrollments."); var e = JourneyEnrollment.Create(tenant.TenantId, c.CompanyId, c.JourneyId, c.CustomerId, c.IdempotencyKey, c.NextRunAt); enrollments.Add(e); return e.Id; }
}
public sealed class RecordAttributionEventCommandHandler(IAttributionEventRepository events, ITenantContext tenant, ICompanyContext company, IClock clock) : ICommandHandler<RecordAttributionEventCommand, Guid>
{
    public Task<Guid> HandleAsync(RecordAttributionEventCommand c, CancellationToken ct = default)
    { if (company.CompanyId != c.CompanyId) throw new InvalidOperationException("The marketing company is not active."); var e = AttributionEvent.Record(tenant.TenantId, c.CompanyId, c.CampaignId, c.MessageId, c.CustomerId, c.EventType, clock.UtcNow); events.Add(e); return Task.FromResult(e.Id); }
}
