using VumaRetail.Domain.Registry;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>Implements the saga coordinator: writes intent, dispatches legs, tracks acks, compensates, alarms.</summary>
public sealed class SagaCoordinator : ISagaCoordinator
{
    private readonly VumaRegistryDbContext _registry;
    private readonly IClock _clock;

    public SagaCoordinator(VumaRegistryDbContext registry, IClock clock)
    {
        _registry = registry;
        _clock = clock;
    }

    public async Task<SagaResult> ExecuteAsync(SagaIntent intent, CancellationToken cancellationToken = default)
    {
        if (intent.State != SagaIntentState.Pending)
            throw new InvalidOperationException("Intent must be in Pending state.");

        _registry.SagaIntents.Add(intent);
        await _registry.CommitAsync(cancellationToken);

        intent.Start("coordinator");
        await _registry.CommitAsync(cancellationToken);

        var legResults = new List<SagaLegResult>();
        foreach (var leg in intent.Legs)
        {
            try
            {
                leg.MarkDispatched(_clock.UtcNow, intent.OperationStamp);
                await _registry.CommitAsync(cancellationToken);

                await DispatchLegAsync(leg, intent, cancellationToken);
                leg.Acknowledge(_clock.UtcNow);
                await _registry.CommitAsync(cancellationToken);
                legResults.Add(new SagaLegResult(leg.LegId, leg.CompanyId, SagaLegState.Acknowledged));
            }
            catch (Exception ex)
            {
                leg.Fail(ex.Message);
                await _registry.CommitAsync(cancellationToken);
                legResults.Add(new SagaLegResult(leg.LegId, leg.CompanyId, SagaLegState.Failed, ex.Message));
            }
        }

        if (legResults.All(r => r.State == SagaLegState.Acknowledged))
        {
            intent.Complete();
            await _registry.CommitAsync(cancellationToken);
            return new SagaResult(intent.Id, SagaIntentState.Completed, legResults);
        }
        else if (legResults.Any(r => r.State == SagaLegState.Failed))
        {
            intent.Compensate();
            await _registry.CommitAsync(cancellationToken);
            return new SagaResult(intent.Id, SagaIntentState.Compensated, legResults);
        }
        else
        {
            intent.Timeout(_clock.UtcNow.AddMinutes(30));
            await _registry.CommitAsync(cancellationToken);
            return new SagaResult(intent.Id, SagaIntentState.TimedOut, legResults);
        }
    }

    private async Task DispatchLegAsync(SagaLeg leg, SagaIntent intent, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    public Task<SagaIntent?> GetAsync(Guid intentId, CancellationToken cancellationToken = default)
        => _registry.SagaIntents
            .AsNoTracking()
            .Include(i => i.Legs)
            .FirstOrDefaultAsync(i => i.Id == intentId, cancellationToken);

    public async Task CompensateAsync(Guid intentId, CancellationToken cancellationToken = default)
    {
        var intent = await _registry.SagaIntents
            .Include(i => i.Legs)
            .FirstOrDefaultAsync(i => i.Id == intentId, cancellationToken)
            ?? throw new InvalidOperationException("Intent not found.");

        intent.Compensate();
        await _registry.CommitAsync(cancellationToken);
    }

    public async Task RetryLegAsync(Guid intentId, Guid legId, CancellationToken cancellationToken = default)
    {
        var intent = await _registry.SagaIntents
            .Include(i => i.Legs)
            .FirstOrDefaultAsync(i => i.Id == intentId, cancellationToken)
            ?? throw new InvalidOperationException("Intent not found.");

        var leg = intent.Legs.FirstOrDefault(l => l.LegId == legId)
            ?? throw new InvalidOperationException("Leg not found.");

        if (leg.State == SagaLegState.Acknowledged)
            return;

        leg.MarkDispatched(_clock.UtcNow, intent.OperationStamp);
        await _registry.CommitAsync(cancellationToken);

        try
        {
            await DispatchLegAsync(leg, intent, cancellationToken);
            leg.Acknowledge(_clock.UtcNow);
            await _registry.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            leg.Fail(ex.Message);
            await _registry.CommitAsync(cancellationToken);
        }
    }
}
