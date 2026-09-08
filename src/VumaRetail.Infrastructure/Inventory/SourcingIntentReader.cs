using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Inventory;

/// <summary>Reads saga intent state for the sourcing ops surface.</summary>
/// <param name="registry">The registry database.</param>
public sealed class SourcingIntentReader(VumaRegistryDbContext registry) : ISourcingIntentReader
{
    /// <inheritdoc />
    public async Task<SourcingIntentResult?> FindAsync(Guid intentId, CancellationToken cancellationToken = default)
    {
        SagaIntent? intent = await registry.SagaIntents
            .AsNoTracking()
            .Include(current => current.Legs)
            .FirstOrDefaultAsync(current => current.Id == intentId, cancellationToken)
            .ConfigureAwait(false);

        if (intent is null)
        {
            return null;
        }

        return new SourcingIntentResult(
            intent.Id,
            intent.Type,
            intent.State.ToString(),
            intent.CreatedAt,
            [.. intent.Legs
                .OrderBy(leg => leg.CompanyId)
                .Select(leg => new SourcingIntentLegResult(
                    leg.LegId,
                    leg.CompanyId,
                    leg.State.ToString(),
                    leg.Attempts,
                    leg.LastError))]);
    }
}
