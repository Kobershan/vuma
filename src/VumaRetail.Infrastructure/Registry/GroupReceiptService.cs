using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>
/// Orchestrates group receipt capture, allocation, retry and reversal in the registry.
/// Legs execute in their companies' databases through <see cref="GroupReceiptLegDispatcher"/>
/// (ADR-104, ADR-116); this service touches only the registry itself.
/// </summary>
/// <remarks>
/// An application service, NOT a command handler: one allocation spans the registry plus one
/// company's database, and a reversal spans several — each leg posts in its own company scope,
/// in its own serialisable transaction, while this service owns the registry-side intent rows.
/// The saga shape mirrors <c>MixedBasketCompletionService</c> deliberately: intent with an
/// idempotency key, link checks before any write, legs in company order, replay returns stored
/// results without touching a company database.
/// </remarks>
public sealed class GroupReceiptService : IGroupReceiptService
{
    private readonly IGroupReceiptRepository _repository;
    private readonly VumaRegistryDbContext _registry;
    private readonly ICompanyLinkGuard _linkGuard;
    private readonly GroupReceiptLegDispatcher _legs;
    private readonly IHybridClock _hybridClock;
    private readonly IPrincipalAccessor _principal;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GroupReceiptService> _logger;

    public GroupReceiptService(
        IGroupReceiptRepository repository,
        VumaRegistryDbContext registry,
        ICompanyLinkGuard linkGuard,
        GroupReceiptLegDispatcher legs,
        IHybridClock hybridClock,
        IPrincipalAccessor principal,
        IClock clock,
        IUnitOfWork unitOfWork,
        ILogger<GroupReceiptService> logger)
    {
        _repository = repository;
        _registry = registry;
        _linkGuard = linkGuard;
        _legs = legs;
        _hybridClock = hybridClock;
        _principal = principal;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Guid> CaptureAsync(
        Guid tenantId, Guid capturingCompanyId, Guid bankAccountId,
        Money amount, string tenderType, string reference, DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default)
    {
        GroupReceipt receipt = GroupReceipt.Capture(
            tenantId, capturingCompanyId, bankAccountId, amount, tenderType, reference, capturedAt);

        await _repository.AddAsync(receipt, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);
        return receipt.Id;
    }

    public async Task AllocateAsync(
        Guid tenantId, Guid groupReceiptId, Guid companyId,
        Guid? customerPartnerId, Money amount,
        IReadOnlyList<Guid>? targetInvoiceIds,
        CancellationToken cancellationToken = default)
    {
        GroupReceipt receipt = await _repository.GetByIdAsync(groupReceiptId, cancellationToken)
            ?? throw new InvalidOperationException($"Group receipt {groupReceiptId} not found.");
        EnsureTenant(receipt.TenantId, tenantId);

        // Check company link has SharedReceipting scope (ADR-122). A basket that could not be
        // completed is never allowed to be built — the refusal happens here, before any write.
        await _linkGuard.RequireLinkAsync(tenantId, receipt.CapturingCompanyId, companyId,
            CompanyLinkScope.SharedReceipting, cancellationToken);

        // Allocate in the domain aggregate (enforces Σ ≤ captured).
        GroupReceiptAllocation allocation = receipt.Allocate(companyId, customerPartnerId, amount, targetInvoiceIds);

        Guid operatorId = await RequireOrderingOperatorAsync(tenantId, receipt.CapturingCompanyId, cancellationToken)
            .ConfigureAwait(false);

        // The intent carries its own idempotency key per allocation: a retried call with the
        // same allocation id reuses these rows through RetryAllocationAsync, never re-keys.
        SagaIntent intent = SagaIntent.Create(
            tenantId,
            GroupReceiptLegDispatcher.AllocationIntentType,
            $"{groupReceiptId:N}:{allocation.Id:N}",
            _clock.UtcNow,
            $"{{\"groupReceiptId\":\"{groupReceiptId}\",\"allocationId\":\"{allocation.Id}\",\"companyId\":\"{companyId}\"}}");
        intent.Authorize(operatorId, _principal.Principal, _hybridClock.Next().ToString());
        intent.AddLeg(companyId, allocation.Id);

        InterCompanyClearingIntent? clearing = null;
        if (companyId != receipt.CapturingCompanyId)
        {
            clearing = InterCompanyClearingIntent.Create(
                tenantId, receipt.Id, "group-receipt",
                receipt.CapturingCompanyId, companyId, amount, amount.Currency,
                allocation.Id);
            await _repository.AddAsync(clearing, cancellationToken);
        }

        _registry.SagaIntents.Add(intent);
        await _repository.UpdateAsync(receipt, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        intent.Start("group-receipt-allocation");
        await _unitOfWork.CommitAsync(cancellationToken);

        await DispatchAllocationAsync(intent, receipt, allocation, clearing, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RetryAllocationAsync(
        Guid tenantId, Guid groupReceiptId, Guid allocationId,
        CancellationToken cancellationToken = default)
    {
        GroupReceipt receipt = await _repository.GetByIdAsync(groupReceiptId, cancellationToken)
            ?? throw new InvalidOperationException($"Group receipt {groupReceiptId} not found.");
        EnsureTenant(receipt.TenantId, tenantId);

        GroupReceiptAllocation allocation = receipt.Allocations.FirstOrDefault(a => a.Id == allocationId)
            ?? throw new InvalidOperationException($"Allocation {allocationId} not found.");

        // An applied leg is acknowledged without touching the company database: retry is
        // always safe, and a retry after success is a no-op, never a second posting.
        if (allocation.LegState == GroupReceiptAllocationLegState.Applied)
        {
            return;
        }

        if (allocation.LegState == GroupReceiptAllocationLegState.Compensated)
        {
            throw new InvalidOperationException($"Allocation {allocationId} was compensated; allocate again instead.");
        }

        // Links lapse: re-check before every write, not just at first allocation (TRADING_GROUP.md §2).
        await _linkGuard.RequireLinkAsync(tenantId, receipt.CapturingCompanyId, allocation.CompanyId,
            CompanyLinkScope.SharedReceipting, cancellationToken);

        Guid operatorId = await RequireOrderingOperatorAsync(tenantId, receipt.CapturingCompanyId, cancellationToken)
            .ConfigureAwait(false);

        string key = $"{groupReceiptId:N}:{allocation.Id:N}";
        SagaIntent? intent = await _registry.SagaIntents
            .Include(i => i.Legs)
            .FirstOrDefaultAsync(
                i => i.TenantId == tenantId
                    && i.Type == GroupReceiptLegDispatcher.AllocationIntentType
                    && i.IdempotencyKey == key,
                cancellationToken)
            .ConfigureAwait(false);

        if (intent is null)
        {
            intent = SagaIntent.Create(
                tenantId,
                GroupReceiptLegDispatcher.AllocationIntentType,
                key,
                _clock.UtcNow,
                $"{{\"groupReceiptId\":\"{groupReceiptId}\",\"allocationId\":\"{allocation.Id}\",\"companyId\":\"{allocation.CompanyId}\"}}");
            intent.Authorize(operatorId, _principal.Principal, _hybridClock.Next().ToString());
            intent.AddLeg(allocation.CompanyId, allocation.Id);
            _registry.SagaIntents.Add(intent);
            await _unitOfWork.CommitAsync(cancellationToken);
        }

        InterCompanyClearingIntent? clearing = null;
        if (allocation.CompanyId != receipt.CapturingCompanyId)
        {
            clearing = await FindClearingIntentAsync(tenantId, receipt.Id, allocation, cancellationToken)
                .ConfigureAwait(false);
            if (clearing is null)
            {
                clearing = InterCompanyClearingIntent.Create(
                    tenantId, receipt.Id, "group-receipt",
                    receipt.CapturingCompanyId, allocation.CompanyId, allocation.Amount, allocation.Amount.Currency,
                    allocation.Id);
                await _repository.AddAsync(clearing, cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
            }
        }

        if (intent.State == SagaIntentState.Pending)
        {
            intent.Start("group-receipt-allocation");
            await _unitOfWork.CommitAsync(cancellationToken);
        }

        await DispatchAllocationAsync(intent, receipt, allocation, clearing, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ReverseAsync(
        Guid tenantId, Guid groupReceiptId,
        CancellationToken cancellationToken = default)
    {
        GroupReceipt receipt = await _repository.GetByIdAsync(groupReceiptId, cancellationToken)
            ?? throw new InvalidOperationException($"Group receipt {groupReceiptId} not found.");
        EnsureTenant(receipt.TenantId, tenantId);

        // Reversing twice returns the stored outcome: reversal legs are idempotent on their
        // deterministic ids, so a replayed reverse creates nothing.
        if (receipt.Status == GroupReceiptStatus.Reversed)
        {
            return;
        }

        Guid operatorId = await RequireOrderingOperatorAsync(tenantId, receipt.CapturingCompanyId, cancellationToken)
            .ConfigureAwait(false);

        // Reversal is idempotent on its own key: a replayed reverse finds the completed intent
        // and returns, while a retry after a partial failure reuses the open intent and drives
        // only the legs that never acknowledged (recovery is retry, never re-key).
        string key = $"{groupReceiptId:N}:reversal";
        SagaIntent? intent = await _registry.SagaIntents
            .Include(i => i.Legs)
            .FirstOrDefaultAsync(
                i => i.TenantId == tenantId
                    && i.Type == GroupReceiptLegDispatcher.ReversalIntentType
                    && i.IdempotencyKey == key,
                cancellationToken)
            .ConfigureAwait(false);

        if (intent is { State: SagaIntentState.Completed })
        {
            return;
        }

        if (intent is null)
        {
            List<GroupReceiptAllocation> applied = receipt.Allocations
                .Where(a => a.LegState == GroupReceiptAllocationLegState.Applied)
                .OrderBy(a => a.CompanyId)
                .ToList();

            // Nothing ever posted anywhere: no reversal legs exist, so no saga intent either.
            // Compensate the pending legs below, mark the receipt reversed, and return.
            if (applied.Count == 0)
            {
                await CompensatePendingAllocationsAsync(tenantId, receipt, cancellationToken)
                    .ConfigureAwait(false);
                receipt.Reverse();
                await _repository.UpdateAsync(receipt, cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
                return;
            }

            intent = SagaIntent.Create(
                tenantId,
                GroupReceiptLegDispatcher.ReversalIntentType,
                key,
                _clock.UtcNow,
                $"{{\"groupReceiptId\":\"{groupReceiptId}\"}}");
            intent.Authorize(operatorId, _principal.Principal, _hybridClock.Next().ToString());

            foreach (GroupReceiptAllocation allocation in applied)
            {
                intent.AddLeg(allocation.CompanyId, allocation.Id);
            }

            _registry.SagaIntents.Add(intent);
            await _unitOfWork.CommitAsync(cancellationToken);
        }

        if (intent.State == SagaIntentState.Pending)
        {
            intent.Start("group-receipt-reversal");
            await _unitOfWork.CommitAsync(cancellationToken);
        }

        try
        {
            foreach (SagaLeg leg in intent.Legs.OrderBy(l => l.CompanyId))
            {
                if (leg.State == SagaLegState.Acknowledged)
                {
                    continue;
                }

                GroupReceiptAllocation? allocation = receipt.Allocations.FirstOrDefault(a => a.Id == leg.LegId);
                if (allocation is not { LegState: GroupReceiptAllocationLegState.Applied })
                {
                    // Already compensated by an earlier attempt; nothing left to reverse here.
                    leg.Acknowledge(_clock.UtcNow);
                    continue;
                }

                InterCompanyClearingIntent? clearing = null;
                if (allocation.CompanyId != receipt.CapturingCompanyId)
                {
                    clearing = await FindClearingIntentAsync(tenantId, receipt.Id, allocation, cancellationToken)
                        .ConfigureAwait(false);
                }

                leg.MarkDispatched(_clock.UtcNow, intent.OperationStamp);
                await _legs.ExecuteReceiptReversalLegAsync(receipt, allocation, intent.Id, cancellationToken)
                    .ConfigureAwait(false);

                if (clearing is not null)
                {
                    foreach (InterCompanyClearingLeg clearingLeg in clearing.Legs.OrderBy(l => l.CompanyId))
                    {
                        await _legs.ExecuteClearingReversalLegAsync(clearing, clearingLeg, intent.Id, cancellationToken)
                            .ConfigureAwait(false);
                        clearing.AcknowledgeLeg(clearingLeg.Id);
                    }

                    clearing.Reverse();
                    await _repository.UpdateAsync(clearing, cancellationToken);
                }

                receipt.CompensateAllocation(allocation.Id);
                leg.Acknowledge(_clock.UtcNow);
                await _unitOfWork.CommitAsync(cancellationToken);
            }

            // Allocations that never applied have no company-side documents; close their saga
            // legs and their clearing intents so a timed-out intent can never be redriven after
            // the receipt reverses, and nothing dangles on the outstanding report.
            await CompensatePendingAllocationsAsync(tenantId, receipt, cancellationToken)
                .ConfigureAwait(false);

            receipt.Reverse();
            await _repository.UpdateAsync(receipt, cancellationToken);
            intent.Complete();
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch (Exception failure)
        {
            _logger.LogWarning(
                failure,
                "Group receipt {ReceiptId} reversal failed partway; applied legs keep their reversing documents, pending legs stay pending.",
                groupReceiptId);

            foreach (SagaLeg leg in intent.Legs.Where(l => l.State is SagaLegState.Dispatched or SagaLegState.Failed))
            {
                leg.Fail(failure.Message);
            }

            await _unitOfWork.CommitAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Group receipt {groupReceiptId} reversal failed: {failure.Message}", failure);
        }
    }

    /// <summary>
    /// Closes saga legs and clearing intents for allocations that never applied: they have no
    /// company-side documents, but their intents would otherwise dangle on the outstanding report
    /// or be redriven after the receipt reverses.
    /// </summary>
    private async Task CompensatePendingAllocationsAsync(
        Guid tenantId, GroupReceipt receipt, CancellationToken cancellationToken)
    {
        foreach (GroupReceiptAllocation pending in receipt.Allocations
            .Where(a => a.LegState == GroupReceiptAllocationLegState.Pending))
        {
            SagaIntent? open = await _registry.SagaIntents
                .Include(i => i.Legs)
                .FirstOrDefaultAsync(
                    i => i.TenantId == tenantId
                        && i.Type == GroupReceiptLegDispatcher.AllocationIntentType
                        && i.IdempotencyKey == $"{receipt.Id:N}:{pending.Id:N}",
                    cancellationToken)
                .ConfigureAwait(false);
            if (open is { State: SagaIntentState.InProgress })
            {
                open.Compensate();
            }

            if (pending.CompanyId != receipt.CapturingCompanyId)
            {
                InterCompanyClearingIntent? pendingClearing = await FindClearingIntentAsync(
                        tenantId, receipt.Id, pending, cancellationToken)
                    .ConfigureAwait(false);
                if (pendingClearing is not null
                    && pendingClearing.State is InterCompanyClearingIntentState.Pending
                        or InterCompanyClearingIntentState.PartiallySettled)
                {
                    pendingClearing.Compensate();
                    await _repository.UpdateAsync(pendingClearing, cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// Runs one allocation's legs in company order and settles the registry rows: receipt leg,
    /// then both clearing legs, then the intent. A leg failure leaves the allocation pending
    /// with the reason recorded — recovery is <see cref="RetryAllocationAsync"/>, never re-key.
    /// </summary>
    private async Task DispatchAllocationAsync(
        SagaIntent intent,
        GroupReceipt receipt,
        GroupReceiptAllocation allocation,
        InterCompanyClearingIntent? clearing,
        CancellationToken cancellationToken)
    {
        SagaLeg leg = intent.Legs.First(l => l.LegId == allocation.Id);

        try
        {
            if (leg.State == SagaLegState.Acknowledged)
            {
                if (allocation.LegState == GroupReceiptAllocationLegState.Pending)
                {
                    receipt.AcknowledgeAllocation(allocation.Id);
                }

                await _unitOfWork.CommitAsync(cancellationToken);
                return;
            }

            leg.MarkDispatched(_clock.UtcNow, intent.OperationStamp);
            await _legs.ExecuteReceiptLegAsync(
                    receipt, allocation, intent.Id, clearing?.Id, cancellationToken)
                .ConfigureAwait(false);

            // Clearing settles before the allocation acknowledges: if the bank-side leg fails,
            // the allocation stays pending and the retry resumes at the clearing leg (the
            // receipt leg is idempotent on its deterministic id, so it posts nothing twice).
            if (clearing is not null)
            {
                foreach (InterCompanyClearingLeg clearingLeg in clearing.Legs.OrderBy(l => l.CompanyId))
                {
                    await _legs.ExecuteClearingLegAsync(clearing, clearingLeg, receipt, allocation, cancellationToken)
                        .ConfigureAwait(false);
                    clearing.AcknowledgeLeg(clearingLeg.Id);
                }

                await _repository.UpdateAsync(clearing, cancellationToken);
            }

            receipt.AcknowledgeAllocation(allocation.Id);
            leg.Acknowledge(_clock.UtcNow);

            await _repository.UpdateAsync(receipt, cancellationToken);
            intent.Complete();
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch (Exception failure)
        {
            _logger.LogWarning(
                failure,
                "Group receipt leg {AllocationId} in company {CompanyId} failed; allocation stays pending for retry.",
                allocation.Id, allocation.CompanyId);

            leg.Fail(failure.Message);
            allocation.ErrorMessage = failure.Message;
            await _repository.UpdateAsync(receipt, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);

            throw new InvalidOperationException(
                $"Allocation {allocation.Id} to company {allocation.CompanyId} failed: {failure.Message}", failure);
        }
    }

    private async Task<Guid> RequireOrderingOperatorAsync(
        Guid tenantId, Guid orderingCompanyId, CancellationToken cancellationToken)
    {
        Company? ordering = await _registry.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                company => company.TenantId == tenantId && company.Id == orderingCompanyId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The capturing company is not registered.");

        if (ordering.OperatorId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The capturing company has no Operator ID; nothing may settle across companies for it (ADR-121).");
        }

        return ordering.OperatorId;
    }

    private async Task<InterCompanyClearingIntent?> FindClearingIntentAsync(
        Guid tenantId, Guid groupReceiptId, GroupReceiptAllocation allocation,
        CancellationToken cancellationToken)
    {
        // Clearing always runs from the bank-owning capturer to the allocated company, and the
        // allocation id is the exact link: one receipt may carry several same-company,
        // same-amount slices, so company-plus-amount is ambiguous. Intents created before the
        // link existed carry Guid.Empty and fall back to the legacy match.
        IReadOnlyList<InterCompanyClearingIntent> intents =
            await _repository.GetClearingIntentsForDocumentAsync(groupReceiptId, cancellationToken);

        return intents.FirstOrDefault(i => i.AllocationId != Guid.Empty && i.AllocationId == allocation.Id)
            ?? intents.FirstOrDefault(i =>
                i.AllocationId == Guid.Empty
                && i.ToCompanyId == allocation.CompanyId
                && i.Amount == allocation.Amount);
    }

    private static void EnsureTenant(Guid receiptTenantId, Guid tenantId)
    {
        if (receiptTenantId != tenantId)
        {
            throw new InvalidOperationException("The group receipt belongs to a different tenant.");
        }
    }
}

/// <summary>
/// Checks that a CompanyLink is Active with the required scope before a cross-company operation (ADR-122).
/// </summary>
public sealed class CompanyLinkGuard : ICompanyLinkGuard
{
    private readonly ICompanyLinkService _linkService;

    public CompanyLinkGuard(ICompanyLinkService linkService)
    {
        _linkService = linkService;
    }

    public async Task RequireLinkAsync(Guid tenantId, Guid companyAId, Guid companyBId,
        CompanyLinkScope requiredScope, CancellationToken cancellationToken = default)
    {
        // Delegate to the existing CompanyLinkService which handles link validation
        await _linkService.RequireLink(companyAId, companyBId, requiredScope, cancellationToken);
    }
}
