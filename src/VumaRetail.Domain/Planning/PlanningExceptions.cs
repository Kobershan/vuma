namespace VumaRetail.Domain.Planning;

/// <summary>Thrown when a planning record cannot be found in this tenant.</summary>
/// <param name="kind">What was looked for.</param>
/// <param name="id">The id that was looked up.</param>
public sealed class PlanningNotFoundException(string kind, Guid id)
    : Exception($"Planning {kind} {id:D} was not found.")
{
    /// <summary>What was looked for.</summary>
    public string Kind { get; } = kind;

    /// <summary>The id that was looked up.</summary>
    public Guid Id { get; } = id;
}

/// <summary>Thrown when a planning operation breaks a domain invariant.</summary>
public sealed class PlanningRuleException : Exception
{
    private PlanningRuleException(string message)
        : base(message)
    {
    }

    /// <summary>Neither or both of item and variant were set where exactly one belongs.</summary>
    public static PlanningRuleException ExactlyOneSku()
        => new("Exactly one of item or variant must be set.");

    /// <summary>A forecast version stamp is not in <c>vN</c> form.</summary>
    /// <param name="version">The offending stamp.</param>
    public static PlanningRuleException BadForecastVersion(string version)
        => new($"Forecast version '{version}' is not in vN form.");

    /// <summary>A calculation input is unusable.</summary>
    /// <param name="reason">Why.</param>
    public static PlanningRuleException BadInput(string reason)
        => new(reason);

    /// <summary>The forecast horizon cannot cover lead time plus review period.</summary>
    /// <param name="horizonDays">The horizon available.</param>
    /// <param name="requiredDays">Lead time plus review period.</param>
    public static PlanningRuleException HorizonTooShort(int horizonDays, int requiredDays)
        => new($"Forecast horizon ({horizonDays} days) cannot cover lead time plus review period ({requiredDays} days).");

    /// <summary>A state transition the lifecycle forbids.</summary>
    /// <param name="entity">What was transitioned.</param>
    /// <param name="from">Where it stands.</param>
    /// <param name="to">Where it was asked to go.</param>
    public static PlanningRuleException BadTransition(string entity, string from, string to)
        => new($"{entity} cannot move from {from} to {to}.");

    /// <summary>An operation on a suggestion that is no longer open.</summary>
    /// <param name="status">Where the suggestion stands.</param>
    public static PlanningRuleException SuggestionNotOpen(SuggestionStatus status)
        => new($"Suggestion is {status}, not Open.");
}

/// <summary>Thrown when a planning write would duplicate a natural key.</summary>
public sealed class PlanningConflictException : Exception
{
    private PlanningConflictException(string message)
        : base(message)
    {
    }

    /// <summary>A duplicate markdown plan for a SKU that already has a live one.</summary>
    /// <param name="sku">Which SKU.</param>
    public static PlanningConflictException LiveMarkdownPlanExists(string sku)
        => new($"A live markdown plan already exists for {sku}.");
}
