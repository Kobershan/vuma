using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions.FieldSales;
using VumaRetail.Domain.FieldSales;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of the field-sales repositories (Stage 14b).</summary>
public sealed class ProFormaOrderRepository(VumaRetailDbContext context) : IProFormaOrderRepository
{
    /// <inheritdoc />
    public async Task<ProFormaOrder?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ProFormaOrder? order = await context.ProFormaOrders
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (order is null)
        {
            return null;
        }

        await context.ProFormaOrderLines
            .Where(line => line.ProFormaOrderId == id)
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        return order;
    }

    /// <inheritdoc />
    public Task<ProFormaOrder?> FindByNumberAsync(string number, CancellationToken cancellationToken = default)
        => context.ProFormaOrders
            .FirstOrDefaultAsync(o => o.ProFormaNumber == number, cancellationToken);

    /// <inheritdoc />
    public Task<ProFormaOrder?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        => context.ProFormaOrders
            .FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProFormaOrder>> ListForRepAsync(Guid repId, CancellationToken cancellationToken = default)
        => await context.ProFormaOrders
            .Where(o => o.RepId == repId)
            .OrderByDescending(o => o.CapturedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProFormaOrder>> ListConvertibleAsync(Guid repId, CancellationToken cancellationToken = default)
        => await context.ProFormaOrders
            .Where(o => o.RepId == repId
                && (o.Status == ProFormaStatus.Draft
                    || o.Status == ProFormaStatus.Submitted
                    || o.Status == ProFormaStatus.Amended))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(ProFormaOrder order)
    {
        context.ProFormaOrders.Add(order);
        foreach (ProFormaOrderLine line in order.Lines)
        {
            context.ProFormaOrderLines.Add(line);
        }
    }
}

/// <summary>EF Core implementation of <see cref="IProFormaCreditNoteRepository"/> (Stage 14b).</summary>
public sealed class ProFormaCreditNoteRepository(VumaRetailDbContext context) : IProFormaCreditNoteRepository
{
    /// <inheritdoc />
    public async Task<ProFormaCreditNote?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ProFormaCreditNote? note = await context.ProFormaCreditNotes
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (note is null)
        {
            return null;
        }

        await context.ProFormaCreditNoteLines
            .Where(line => line.ProFormaCreditNoteId == id)
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        return note;
    }

    /// <inheritdoc />
    public Task<ProFormaCreditNote?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        => context.ProFormaCreditNotes
            .FirstOrDefaultAsync(n => n.IdempotencyKey == idempotencyKey, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProFormaCreditNote>> ListConvertibleAsync(Guid repId, CancellationToken cancellationToken = default)
        => await context.ProFormaCreditNotes
            .Where(n => n.RepId == repId
                && (n.Status == ProFormaStatus.Draft
                    || n.Status == ProFormaStatus.Submitted
                    || n.Status == ProFormaStatus.Amended))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProFormaCreditNote>> ListForRepAsync(Guid repId, CancellationToken cancellationToken = default)
        => await context.ProFormaCreditNotes
            .Where(n => n.RepId == repId)
            .OrderByDescending(n => n.CapturedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(ProFormaCreditNote note)
    {
        context.ProFormaCreditNotes.Add(note);
        foreach (ProFormaCreditNoteLine line in note.Lines)
        {
            context.ProFormaCreditNoteLines.Add(line);
        }
    }
}

/// <summary>EF Core implementation of <see cref="IRepRepository"/> (Stage 14b).</summary>
public sealed class RepRepository(VumaRetailDbContext context) : IRepRepository
{
    /// <inheritdoc />
    public Task<Rep?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => context.Reps.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Rep?> FindByUserAsync(Guid registryUserId, CancellationToken cancellationToken = default)
        => context.Reps.FirstOrDefaultAsync(r => r.RegistryUserId == registryUserId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Rep>> ListAllAsync(CancellationToken cancellationToken = default)
        => await context.Reps.OrderBy(r => r.DisplayName).ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepTarget>> ListTargetsAsync(Guid repId, CancellationToken cancellationToken = default)
        => await context.RepTargets
            .Where(t => t.RepId == repId)
            .OrderByDescending(t => t.PeriodStart)
            .ThenByDescending(t => t.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<RepTarget?> FindCurrentTargetAsync(Guid repId, Guid companyId, DateOnly periodStart, CancellationToken cancellationToken = default)
        => context.RepTargets.FirstOrDefaultAsync(
            t => t.RepId == repId && t.PeriodStart == periodStart && t.IsCurrent
                && (t.CompanyId == companyId || t.CompanyId == null),
            cancellationToken);

    /// <inheritdoc />
    public void AddRep(Rep rep) => context.Reps.Add(rep);

    /// <inheritdoc />
    public void AddTarget(RepTarget target) => context.RepTargets.Add(target);
}

/// <summary>EF Core implementation of <see cref="IRepPerformanceRepository"/> (Stage 14b).</summary>
public sealed class RepPerformanceRepository(VumaRetailDbContext context) : IRepPerformanceRepository
{
    /// <inheritdoc />
    public Task<RepPerformanceSnapshot?> FindAsync(Guid repId, Guid? companyId, DateOnly periodStart, int version, CancellationToken cancellationToken = default)
        => context.RepPerformanceSnapshots.FirstOrDefaultAsync(
            s => s.RepId == repId && s.CompanyId == companyId && s.PeriodStart == periodStart && s.Version == version,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepPerformanceSnapshot>> ListVersionsAsync(Guid repId, Guid? companyId, DateOnly periodStart, CancellationToken cancellationToken = default)
        => await context.RepPerformanceSnapshots
            .Where(s => s.RepId == repId && s.CompanyId == companyId && s.PeriodStart == periodStart)
            .OrderBy(s => s.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<RepPerformanceSnapshot?> FindLatestAsync(Guid repId, Guid? companyId, DateOnly periodStart, CancellationToken cancellationToken = default)
        => context.RepPerformanceSnapshots
            .Where(s => s.RepId == repId && s.CompanyId == companyId && s.PeriodStart == periodStart)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(RepPerformanceSnapshot snapshot) => context.RepPerformanceSnapshots.Add(snapshot);
}
