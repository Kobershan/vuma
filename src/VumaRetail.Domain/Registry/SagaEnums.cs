namespace VumaRetail.Domain.Registry;

/// <summary>
/// Where a <see cref="SagaIntent"/> stands. The state machine itself belongs to 06d's
/// `ISagaCoordinator` — this enum exists here only because the intent row does (ADR-116).
/// </summary>
public enum SagaIntentStatus
{
    /// <summary>Recorded, in full, before any leg has been dispatched.</summary>
    Raised = 0,

    /// <summary>At least one leg has been dispatched and not every leg has acknowledged.</summary>
    InProgress = 1,

    /// <summary>Every leg has acknowledged. Terminal.</summary>
    Settled = 2,

    /// <summary>A leg cannot be completed and compensation has been raised for the legs that did apply.</summary>
    Compensating = 3,

    /// <summary>Every applied leg has been compensated. Terminal.</summary>
    Compensated = 4,

    /// <summary>A leg did not acknowledge inside its timeout. Raises an operational alarm; not terminal —
    /// dispatch keeps retrying underneath the alarm.</summary>
    Alarmed = 5,
}

/// <summary>Where one <see cref="SagaLeg"/> stands, keyed with its intent by <c>(intent_id, leg_id)</c>.</summary>
public enum SagaLegStatus
{
    /// <summary>Recorded, not yet dispatched.</summary>
    Pending = 0,

    /// <summary>Dispatched to its company database; acknowledgement not yet received.</summary>
    Dispatched = 1,

    /// <summary>The company database confirmed the leg applied. Terminal on the happy path.</summary>
    Acknowledged = 2,

    /// <summary>A compensating document has been dispatched for this leg.</summary>
    Compensating = 3,

    /// <summary>The compensating document was acknowledged. Terminal.</summary>
    Compensated = 4,

    /// <summary>Outstanding past its timeout. Raised alongside the intent's own <see cref="SagaIntentStatus.Alarmed"/>.</summary>
    Alarmed = 5,
}
