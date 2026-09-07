namespace VumaRetail.Application.Inventory.Sourcing;

using VumaRetail.Domain.Inventory.Sourcing;
using VumaRetail.Domain.Registry;

/// <summary>
/// Application service to commit a sourcing plan.
/// NOT a command handler (to respect multi-company boundaries).
/// Orchestrates the saga: link validation, intent creation, per-leg reservation, compensation, order creation.
/// </summary>
public sealed class SourcingCommitService
{
    private readonly CompanyLinkService _linkService;
    private readonly ICompanyReservationGateway _reservationGateway;
    private readonly ISplitDocumentBuilder _splitBuilder;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAlarmService _alarmService;
    private readonly IVumaRegistryDbContext _registryContext;

    public SourcingCommitService(
        CompanyLinkService linkService,
        ICompanyReservationGateway reservationGateway,
        ISplitDocumentBuilder splitBuilder,
        IUnitOfWork unitOfWork,
        IAlarmService alarmService,
        IVumaRegistryDbContext registryContext)
    {
        _linkService = linkService ?? throw new ArgumentNullException(nameof(linkService));
        _reservationGateway = reservationGateway ?? throw new ArgumentNullException(nameof(reservationGateway));
        _splitBuilder = splitBuilder ?? throw new ArgumentNullException(nameof(splitBuilder));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _alarmService = alarmService ?? throw new ArgumentNullException(nameof(alarmService));
        _registryContext = registryContext ?? throw new ArgumentNullException(nameof(registryContext));
    }

    /// <summary>
    /// Commit a sourcing plan: reserve inventory across companies and create split orders.
    /// Returns the committed plan and reservation IDs, or throws if any leg fails.
    /// </summary>
    public async Task<SourcingCommitResult> CommitAsync(
        SourcingPlan plan,
        Guid orderingCompanyId,
        Guid operatorId,
        string idempotencyKey,
        IReadOnlyList<Orders.Domain.SalesOrderLine> sourceOrderLines,
        CancellationToken ct)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (orderingCompanyId == Guid.Empty) throw new ArgumentException("Ordering company ID is required.", nameof(orderingCompanyId));
        if (operatorId == Guid.Empty) throw new ArgumentException("Operator ID is required.", nameof(operatorId));
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));

        // Step 1: Validate links for all supplying companies
        var supplierCompanies = plan.Lines
            .Where(l => l.Allocation.PlannedQuantity > 0)
            .Select(l => l.CompanyId)
            .Distinct()
            .ToList();

        foreach (var supplier in supplierCompanies)
        {
            if (supplier != orderingCompanyId)
            {
                // ADR-122: require SharedSourcing link only at commit time
                await _linkService.RequireLinkAsync(
                    orderingCompanyId,
                    supplier,
                    "SharedSourcing",
                    ct);
            }
        }

        // Step 2: Create saga intent in registry
        var intent = SagaIntent.Create(
            _unitOfWork.TenantId,
            "availability.sourcing-commit",
            idempotencyKey,
            DateTime.UtcNow);

        intent.Authorize(operatorId, nameof(SourcingCommitService), _unitOfWork.HlcStamp.ToString());

        // Add one leg per supplying company
        var legIds = new Dictionary<Guid, Guid>();
        foreach (var supplier in supplierCompanies)
        {
            var legId = UuidV7.NewGuid();
            intent.AddLeg(supplier, legId);
            legIds[supplier] = legId;
        }

        // Persist intent before attempting reservations
        _registryContext.SagaIntents.Add(intent);
        await _registryContext.SaveChangesAsync(ct);

        intent.Start(nameof(SourcingCommitService));
        _registryContext.SagaIntents.Update(intent);
        await _registryContext.SaveChangesAsync(ct);

        // Step 3: Execute each leg (reserve in each company's DB)
        var reservationResults = new Dictionary<Guid, LegReservationResult>();
        var acknowledgedLegs = new List<Guid>();

        foreach (var supplier in supplierCompanies)
        {
            var leg = intent.Legs.First(l => l.CompanyId == supplier);
            var allocation = plan.Lines.First(l => l.CompanyId == supplier).Allocation;

            try
            {
                var reservedQuantity = await _reservationGateway.ReserveLegAsync(
                    supplier,
                    leg.LegId,
                    intent.Id,
                    plan.Demand.ItemReference,
                    allocation.PlannedQuantity,
                    ReservationSource.Order,
                    plan.OrderLineId,
                    ct);

                reservationResults[supplier] = new LegReservationResult
                {
                    Succeeded = true,
                    ReservedQuantity = reservedQuantity
                };

                leg.Acknowledge();
                acknowledgedLegs.Add(supplier);
            }
            catch (Exception ex)
            {
                // Short leg or other failure: mark for compensation
                reservationResults[supplier] = new LegReservationResult
                {
                    Succeeded = false,
                    Error = ex.Message
                };
            }
        }

        // Step 4: Handle short legs (re-source if first failure)
        var shortLegs = reservationResults.Where(r => !r.Value.Succeeded).ToList();
        if (shortLegs.Any())
        {
            // Re-source the shortfall once from remaining companies
            var shortfallQuantity = plan.Lines
                .Where(l => shortLegs.Any(s => s.Key == l.CompanyId))
                .Sum(l => l.Allocation.PlannedQuantity);

            if (shortfallQuantity > 0 && supplierCompanies.Count > 1)
            {
                var remainingSuppliers = supplierCompanies.Where(s => !shortLegs.Any(k => k.Key == s)).ToList();
                foreach (var remaining in remainingSuppliers.Where(r => reservationResults[r].Succeeded))
                {
                    if (shortfallQuantity <= 0) break;

                    var available = reservationResults[remaining].ReservedQuantity ?? 0;
                    // In a real implementation, re-read actual available and retry once
                    // For now, mark as partial backorder
                    shortfallQuantity -= available;
                }
            }

            // Compensate acknowledged legs
            foreach (var ackLeg in acknowledgedLegs)
            {
                await _reservationGateway.CompensateLegAsync(
                    ackLeg,
                    intent.Id,
                    intent.Legs.First(l => l.CompanyId == ackLeg).LegId,
                    ct);
            }

            intent.Compensate();
            _registryContext.SagaIntents.Update(intent);
            await _registryContext.SaveChangesAsync(ct);

            throw new SourcingRuleException(
                "SOURCING_COMMIT_FAILED",
                $"Sourcing commit failed for order line {plan.OrderLineId}: {string.Join("; ", shortLegs.Select(s => s.Value.Error))}");
        }

        // Step 5: All legs acknowledged; mark intent complete and create split orders
        intent.Complete();
        _registryContext.SagaIntents.Update(intent);
        await _registryContext.SaveChangesAsync(ct);

        // Step 6: Build and persist split documents (one SalesOrder per company)
        var splitOrders = await _splitBuilder.BuildSplitOrdersAsync(
            plan,
            intent.Id,
            sourceOrderLines,
            ct);

        return new SourcingCommitResult
        {
            IntentId = intent.Id,
            CommittedPlan = plan,
            SplitOrderIds = splitOrders.Select(o => o.Id).ToList(),
            ReservationsByCompany = reservationResults.ToDictionary(r => r.Key, r => r.Value.ReservedQuantity ?? 0)
        };
    }

    private sealed class LegReservationResult
    {
        public bool Succeeded { get; set; }
        public decimal? ReservedQuantity { get; set; }
        public string? Error { get; set; }
    }
}

public sealed class SourcingCommitResult
{
    public Guid IntentId { get; set; }
    public SourcingPlan CommittedPlan { get; set; } = null!;
    public List<Guid> SplitOrderIds { get; set; } = new();
    public Dictionary<Guid, decimal> ReservationsByCompany { get; set; } = new();
}
