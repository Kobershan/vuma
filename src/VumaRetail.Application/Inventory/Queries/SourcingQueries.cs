using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Inventory;

namespace VumaRetail.Application.Inventory.Queries;

/// <summary>Reads a sourcing saga intent and its legs for the in-flight report.</summary>
/// <param name="IntentId">The intent.</param>
public sealed record GetSourcingIntentQuery(Guid IntentId) : IQuery<SourcingIntentResult?>;

/// <summary>Reads the intent.</summary>
/// <param name="intents">Intent lookup.</param>
public sealed class GetSourcingIntentQueryHandler(ISourcingIntentReader intents)
    : IQueryHandler<GetSourcingIntentQuery, SourcingIntentResult?>
{
    /// <inheritdoc />
    public Task<SourcingIntentResult?> HandleAsync(GetSourcingIntentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return intents.FindAsync(query.IntentId, cancellationToken);
    }
}
