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

    public async Task SaveAsync(DocumentDeliveryToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        await db.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
