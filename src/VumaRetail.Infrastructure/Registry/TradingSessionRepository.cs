using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Registry.Trading;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>EF Core implementation of <see cref="ITradingSessionRepository"/> (Stage 09b).</summary>
public sealed class TradingSessionRepository(VumaRegistryDbContext context) : ITradingSessionRepository
{
    /// <inheritdoc />
    public async Task<TradingSession?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => await context.TradingSessions
            .Include(session => session.Segments)
            .ThenInclude(segment => segment.Lines)
            .FirstOrDefaultAsync(session => session.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<TradingSession?> FindByNumberAsync(string sessionNumber, CancellationToken cancellationToken = default)
        => await context.TradingSessions
            .Include(session => session.Segments)
            .ThenInclude(segment => segment.Lines)
            .FirstOrDefaultAsync(session => session.SessionNumber == sessionNumber, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<TradingSession?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        => await context.TradingSessions
            .Include(session => session.Segments)
            .ThenInclude(segment => segment.Lines)
            .FirstOrDefaultAsync(session => session.IdempotencyKey == idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TradingSession>> ListOpenAsync(CancellationToken cancellationToken = default)
        => await context.TradingSessions
            .Include(session => session.Segments)
            .ThenInclude(segment => segment.Lines)
            .Where(session => session.Status == TradingSessionStatus.Open
                || session.Status == TradingSessionStatus.Tendered
                || session.Status == TradingSessionStatus.CompletionFailed)
            .OrderBy(session => session.OpenedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(TradingSession session)
        => context.TradingSessions.Add(session);

    /// <inheritdoc />
    public Task<int> CommitSessionAsync(CancellationToken cancellationToken = default)
        => context.SaveChangesAsync(cancellationToken);
}
