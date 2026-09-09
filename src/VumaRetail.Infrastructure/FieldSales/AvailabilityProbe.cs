using VumaRetail.Application.Abstractions.FieldSales;
using VumaRetail.Application.Inventory;

namespace VumaRetail.Infrastructure.FieldSales;

/// <summary>Probes group-wide available for one SKU in one company (Stage 14b).</summary>
public sealed class AvailabilityProbe(IAvailabilityService availability) : IAvailabilityProbe
{
    /// <inheritdoc />
    public async Task<AvailabilityProbeResult> ProbeAsync(
        Guid companyId, Guid? itemId, Guid? itemVariantId,
        CancellationToken cancellationToken = default)
    {
        GroupAvailabilityView view = await availability
            .GetGroupAsync(itemId, itemVariantId, cancellationToken)
            .ConfigureAwait(false);

        GroupAvailabilityContribution? contribution = view.Contributions
            .FirstOrDefault(candidate => candidate.CompanyId == companyId);

        if (contribution is null)
        {
            return new AvailabilityProbeResult(0m, view.AsAt, IsStale: true);
        }

        return new AvailabilityProbeResult(
            contribution.Promise.Available.Value, view.AsAt, contribution.IsStale);
    }
}
