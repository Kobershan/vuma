using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Conversations;
using VumaRetail.Domain.Conversations;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF-backed delivery-token store; token fetch state is committed through the company context.</summary>
public sealed class EfDocumentDeliveryTokenStore(VumaRetailDbContext db) : IDocumentDeliveryTokenStore
{
    public async Task AddAsync(DocumentDeliveryToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        db.DocumentDeliveryTokens.Add(token);
        await db.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<DocumentDeliveryToken?> FindAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return db.DocumentDeliveryTokens.SingleOrDefaultAsync(x => x.Token == token.Trim(), cancellationToken);
    }

    public async Task<DocumentDeliveryToken?> ConsumeAsync(
        string token, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        DocumentDeliveryToken? candidate = await db.DocumentDeliveryTokens
            .SingleOrDefaultAsync(x => x.Token == token.Trim()
                && x.RevokedAt == null
                && x.FetchedAt == null
                && x.ExpiresAt >= at, cancellationToken)
            .ConfigureAwait(false);
        if (candidate is null)
        {
            return null;
        }

        int consumed = await db.DocumentDeliveryTokens
            .Where(x => x.Id == candidate.Id
                && x.RevokedAt == null
                && x.FetchedAt == null
                && x.ExpiresAt >= at)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.FetchedAt, at), cancellationToken)
            .ConfigureAwait(false);
        return consumed == 1 ? candidate : null;
    }

    public async Task SaveAsync(DocumentDeliveryToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        await db.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
