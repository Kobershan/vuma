using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>
/// Orchestrates group receipt capture, allocation, and reversal in the registry.
/// Dispatches legs to company databases via ISagaCoordinator (ADR-104, ADR-116).
/// </summary>
public sealed class GroupReceiptService : IGroupReceiptService
{
    private readonly IGroupReceiptRepository _repository;
    private readonly ISagaCoordinator _sagaCoordinator;
    private readonly ICompanyDbContextFactory _companyDbContextFactory;
    private readonly IFinancialEventPoster _eventPoster;
    private readonly ICompanyLinkGuard _linkGuard;

    public GroupReceiptService(
        IGroupReceiptRepository repository,
        ISagaCoordinator sagaCoordinator,
        ICompanyDbContextFactory companyDbContextFactory,
        IFinancialEventPoster eventPoster,
        ICompanyLinkGuard linkGuard)
    {
        _repository = repository;
        _sagaCoordinator = sagaCoordinator;
        _companyDbContextFactory = companyDbContextFactory;
        _eventPoster = eventPoster;
        _linkGuard = linkGuard;
    }

    public async Task<Guid> CaptureAsync(
        Guid tenantId, Guid capturingCompanyId, Guid bankAccountId,
        Money amount, string tenderType, string reference, DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default)
    {
        GroupReceipt receipt = GroupReceipt.Capture(
            tenantId, capturingCompanyId, bankAccountId, amount, tenderType, reference, capturedAt);

        await _repository.AddAsync(receipt, cancellationToken);
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

        // Check company link has SharedReceipting scope (ADR-122)
        await _linkGuard.RequireLinkAsync(tenantId, receipt.CapturingCompanyId, companyId,
            CompanyLinkScope.SharedReceipting, cancellationToken);

        // Allocate in the domain aggregate (enforces Σ ≤ captured)
        GroupReceiptAllocation allocation = receipt.Allocate(companyId, customerPartnerId, amount, targetInvoiceIds);

        // Dispatch saga leg to the target company's database (ADR-116)
        SagaIntent intent = SagaIntent.Create(tenantId, "group-receipt-allocation",
            $"{groupReceiptId}:{allocation.Id}", DateTimeOffset.UtcNow,
            $"{{\"groupReceiptId\":\"{groupReceiptId}\",\"allocationId\":\"{allocation.Id}\",\"companyId\":\"{companyId}\"}}");
        intent.AddLeg(companyId, allocation.Id);

        await _sagaCoordinator.ExecuteAsync(intent, cancellationToken);

        await _repository.UpdateAsync(receipt, cancellationToken);
    }

    public async Task ReverseAsync(
        Guid tenantId, Guid groupReceiptId,
        CancellationToken cancellationToken = default)
    {
        GroupReceipt receipt = await _repository.GetByIdAsync(groupReceiptId, cancellationToken)
            ?? throw new InvalidOperationException($"Group receipt {groupReceiptId} not found.");

        // Dispatch reversing saga for every company involved (ADR-104, §7 rule 6)
        receipt.Reverse();

        await _repository.UpdateAsync(receipt, cancellationToken);
    }
}

/// <summary>
/// Checks that a CompanyLink is Active with the required scope before a cross-company operation (ADR-122).
/// </summary>
public interface ICompanyLinkGuard
{
    Task RequireLinkAsync(Guid tenantId, Guid companyAId, Guid companyBId,
        CompanyLinkScope requiredScope, CancellationToken cancellationToken = default);
}
