#pragma warning disable CS1591
using VumaRetail.Domain.FieldSales;

namespace VumaRetail.Application.Abstractions.FieldSales;

/// <summary>Reads and writes pro forma orders (Stage 14b).</summary>
/// <remarks>Tracked entities, never <c>Update</c> — the pipeline's unit of work commits what the handler mutated.</remarks>
public interface IProFormaOrderRepository
{
    Task<ProFormaOrder?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProFormaOrder?> FindByNumberAsync(string number, CancellationToken cancellationToken = default);
    Task<ProFormaOrder?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProFormaOrder>> ListForRepAsync(Guid repId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProFormaOrder>> ListConvertibleAsync(Guid repId, CancellationToken cancellationToken = default);
    void Add(ProFormaOrder order);
}

/// <summary>Reads and writes pro forma credit notes (Stage 14b).</summary>
public interface IProFormaCreditNoteRepository
{
    Task<ProFormaCreditNote?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProFormaCreditNote?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProFormaCreditNote>> ListForRepAsync(Guid repId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProFormaCreditNote>> ListConvertibleAsync(Guid repId, CancellationToken cancellationToken = default);
    void Add(ProFormaCreditNote note);
}

/// <summary>Reads and writes reps and their targets (Stage 14b).</summary>
public interface IRepRepository
{
    Task<Rep?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Rep?> FindByUserAsync(Guid registryUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Rep>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RepTarget>> ListTargetsAsync(Guid repId, CancellationToken cancellationToken = default);
    Task<RepTarget?> FindCurrentTargetAsync(Guid repId, Guid companyId, DateOnly periodStart, CancellationToken cancellationToken = default);
    void AddRep(Rep rep);
    void AddTarget(RepTarget target);
}

/// <summary>Reads and writes rep performance snapshots (Stage 14b).</summary>
public interface IRepPerformanceRepository
{
    Task<RepPerformanceSnapshot?> FindAsync(Guid repId, Guid? companyId, DateOnly periodStart, int version, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RepPerformanceSnapshot>> ListVersionsAsync(Guid repId, Guid? companyId, DateOnly periodStart, CancellationToken cancellationToken = default);
    Task<RepPerformanceSnapshot?> FindLatestAsync(Guid repId, Guid? companyId, DateOnly periodStart, CancellationToken cancellationToken = default);
    void Add(RepPerformanceSnapshot snapshot);
}

/// <summary>Probes group-wide available for one SKU in one company (Stage 14b).</summary>
/// <remarks>
/// Capture-time snapshots only. The approver re-reads live through the same port; the figures
/// stay indicative until approval reserves (ADR-107, ADR-109).
/// </remarks>
public interface IAvailabilityProbe
{
    /// <summary>Available quantity and its as-at instant, for one company.</summary>
    Task<AvailabilityProbeResult> ProbeAsync(
        Guid companyId, Guid? itemId, Guid? itemVariantId,
        CancellationToken cancellationToken = default);
}

/// <summary>One company's available for one SKU, with its as-at.</summary>
/// <param name="Available">Available quantity (never negative).</param>
/// <param name="AsAt">When the figure was true.</param>
/// <param name="IsStale">Whether the figure is older than the freshness threshold.</param>
public sealed record AvailabilityProbeResult(decimal Available, DateTimeOffset AsAt, bool IsStale = false);

/// <summary>Runs the approval-to-documents saga for one pro forma (Stage 14b).</summary>
/// <remarks>
/// An application service, NOT a command handler: approval spans several company databases
/// (ADR-116). Implemented in TASK-14B-002; declared here so the thin command handler compiles.
/// </remarks>
public interface IFieldSalesApprovalService
{
    /// <summary>Approves a submitted order pro forma: reprice, hold, reserve, order, invoice.</summary>
    Task<ApprovedProForma> ApproveOrderAsync(Guid proFormaId, string decidedBy, CancellationToken cancellationToken = default);

    /// <summary>Approves a submitted credit pro forma into a sales return.</summary>
    Task<Guid> ApproveCreditNoteAsync(Guid creditNoteId, string decidedBy, CancellationToken cancellationToken = default);
}

/// <summary>What approval created for an order pro forma.</summary>
/// <param name="OrderId">The confirmed sales order.</param>
/// <param name="InvoiceNumbers">The posted invoices' numbers, per company.</param>
/// <param name="RepriceDelta">Repriced total minus quoted total.</param>
public sealed record ApprovedProForma(Guid OrderId, IReadOnlyList<string> InvoiceNumbers, decimal RepriceDelta);
