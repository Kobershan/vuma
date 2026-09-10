using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>
/// Refuses a period close while inter-company work involving the closing company is still
/// outstanding, naming the blocking intents (Stage 07c, MULTI_COMPANY.md §7).
/// </summary>
public sealed class PeriodCloseGuard(IGroupReceiptRepository receipts) : IPeriodCloseGuard
{
    /// <inheritdoc />
    public async Task CheckAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken = default)
    {
        List<Guid> blockers = [];

        IReadOnlyList<InterCompanyClearingIntent> outstanding =
            await receipts.GetOutstandingIntentsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        blockers.AddRange(outstanding
            .Where(i => i.Legs.Any(l =>
                l.CompanyId == companyId
                && l.State is InterCompanyClearingLegState.Pending or InterCompanyClearingLegState.Failed))
            .Select(i => i.Id));

        IReadOnlyList<GroupReceipt> open =
            await receipts.GetUnallocatedAsync(tenantId, cancellationToken).ConfigureAwait(false);
        blockers.AddRange(open
            .SelectMany(r => r.Allocations)
            .Where(a => a.CompanyId == companyId
                && a.LegState == GroupReceiptAllocationLegState.Pending)
            .Select(a => a.GroupReceiptId)
            .Distinct());

        if (blockers.Count > 0)
        {
            throw new PeriodCloseBlockedByIntentsException(blockers.Distinct().ToList());
        }
    }
}
