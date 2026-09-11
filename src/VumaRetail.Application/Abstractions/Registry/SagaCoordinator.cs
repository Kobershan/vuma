#pragma warning disable CS1591
using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>Coordinates cross-company operations through immutable saga intents and idempotent legs.</summary>
public interface ISagaCoordinator
{
    /// <summary>Creates an intent, dispatches each leg to its company, and returns when all legs are acknowledged or the intent times out.</summary>
    Task<SagaResult> ExecuteAsync(SagaIntent intent, CancellationToken cancellationToken = default);
    
    /// <summary>Retrieves the current state of an in-flight intent.</summary>
    Task<SagaIntent?> GetAsync(Guid intentId, CancellationToken cancellationToken = default);
    
    /// <summary>Compensates all unacknowledged legs and marks the intent compensated.</summary>
    Task CompensateAsync(Guid intentId, CancellationToken cancellationToken = default);
    
    /// <summary>Retries a failed leg with exponential backoff.</summary>
    Task RetryLegAsync(Guid intentId, Guid legId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes one saga leg in the owning company database. Implementations must be idempotent on
/// <c>(intent.Id, leg.LegId)</c>; returning successfully is the acknowledgement consumed by the
/// coordinator. Compensation must create a new reversal/release document, never mutate the
/// document made by <see cref="DispatchAsync"/>.
/// </summary>
public interface ISagaLegDispatcher
{
    /// <summary>Whether this dispatcher owns the supplied immutable intent type.</summary>
    bool CanDispatch(string intentType);

    /// <summary>Applies one company-local leg.</summary>
    Task DispatchAsync(SagaIntent intent, SagaLeg leg, CancellationToken cancellationToken = default);

    /// <summary>Applies a compensating company-local document for one acknowledged leg.</summary>
    Task CompensateAsync(SagaIntent intent, SagaLeg leg, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a saga execution.</summary>
public sealed record SagaResult(Guid IntentId, SagaIntentState FinalState, IReadOnlyList<SagaLegResult> Legs)
{
    /// <summary>Whether the saga completed successfully.</summary>
    public bool Succeeded => FinalState == SagaIntentState.Completed;
    /// <summary>Whether compensation is required.</summary>
    public bool RequiresCompensation => FinalState is SagaIntentState.TimedOut or SagaIntentState.InProgress;
}

/// <summary>The outcome of one saga leg.</summary>
public sealed record SagaLegResult(Guid LegId, Guid CompanyId, SagaLegState State, string? Error = null);
