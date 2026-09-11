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
    private readonly IReadOnlyList<ISagaLegDispatcher> _dispatchers;

    public SagaCoordinator(
        VumaRegistryDbContext registry,
        IClock clock,
        IEnumerable<ISagaLegDispatcher> dispatchers)
    {
        _registry = registry;
        _clock = clock;
        _dispatchers = dispatchers.ToArray();
    }

    public async Task<SagaResult> ExecuteAsync(SagaIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        // The immutable intent is the idempotency boundary. A lost caller response therefore
        // resumes the already-recorded operation instead of creating a second one.
        SagaIntent? recorded = await _registry.SagaIntents
            .Include(candidate => candidate.Legs)
            .SingleOrDefaultAsync(candidate => candidate.TenantId == intent.TenantId
                && candidate.IdempotencyKey == intent.IdempotencyKey, cancellationToken);
        if (recorded is not null) return ToResult(recorded);

        if (intent.State != SagaIntentState.Pending) throw new InvalidOperationException("Intent must be in Pending state.");

        _registry.SagaIntents.Add(intent);
        await _registry.CommitAsync(cancellationToken);

        intent.Start("coordinator");
        await _registry.CommitAsync(cancellationToken);

        foreach (var leg in intent.Legs)
        {
            await DispatchAsync(intent, leg, cancellationToken);
        }

        if (intent.Legs.All(leg => leg.State == SagaLegState.Acknowledged))
        {
            intent.Complete();
            await _registry.CommitAsync(cancellationToken);
        }
        // A transient company failure stays visible and retryable. It is never silently
        // compensated merely because the first attempt failed.
        return ToResult(intent);
    }

    private async Task DispatchAsync(SagaIntent intent, SagaLeg leg, CancellationToken cancellationToken)
    {
        if (leg.State == SagaLegState.Acknowledged) return;

        try
        {
            leg.MarkDispatched(_clock.UtcNow, intent.OperationStamp);
            await _registry.CommitAsync(cancellationToken);

            await DispatcherFor(intent).DispatchAsync(intent, leg, cancellationToken);
            leg.Acknowledge(_clock.UtcNow);
            await _registry.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            leg.Fail(exception.Message);
            await _registry.CommitAsync(cancellationToken);
        }
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

        if (intent.State is SagaIntentState.Completed or SagaIntentState.Compensated)
            throw new InvalidOperationException("Only an in-flight or timed-out intent can be compensated.");

        ISagaLegDispatcher dispatcher = DispatcherFor(intent);
        foreach (SagaLeg leg in intent.Legs
            .Where(leg => leg.State == SagaLegState.Acknowledged)
            .OrderByDescending(leg => leg.LastAttemptAt))
        {
            await dispatcher.CompensateAsync(intent, leg, cancellationToken);
            leg.Compensate();
            await _registry.CommitAsync(cancellationToken);
        }

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

        if (intent.State is SagaIntentState.Compensated or SagaIntentState.Completed || leg.State is SagaLegState.Acknowledged or SagaLegState.Compensated) return;

        // Backoff is calculated from the durable attempt count. A scheduler/redriver can invoke
        // this forever; retries do not create a second company-side document because the
        // dispatcher receives the stable intent/leg pair.
        int exponent = Math.Min(Math.Max(leg.Attempts - 1, 0), 8);
        TimeSpan backoff = TimeSpan.FromMilliseconds(50 * (1 << exponent));
        await Task.Delay(backoff, cancellationToken);
        await DispatchAsync(intent, leg, cancellationToken);

        if (intent.Legs.All(candidate => candidate.State == SagaLegState.Acknowledged))
        {
            intent.Complete();
            await _registry.CommitAsync(cancellationToken);
        }
    }

    private ISagaLegDispatcher DispatcherFor(SagaIntent intent)
    {
        ISagaLegDispatcher[] matches = _dispatchers.Where(dispatcher => dispatcher.CanDispatch(intent.Type)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"No saga dispatcher is registered for intent type '{intent.Type}'."),
            _ => throw new InvalidOperationException($"More than one saga dispatcher is registered for intent type '{intent.Type}'."),
        };
    }

    private static SagaResult ToResult(SagaIntent intent)
        => new(intent.Id, intent.State, intent.Legs
            .OrderBy(leg => leg.LegId)
            .Select(leg => new SagaLegResult(leg.LegId, leg.CompanyId, leg.State, leg.LastError))
            .ToArray());
}
