using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Platform;
using VumaRetail.Domain.Platform;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of the Stage 06 <see cref="IStoreRepository"/> port.</summary>
/// <param name="context">The database context.</param>
public sealed class StoreRepository(VumaRetailDbContext context) : IStoreRepository
{
    /// <inheritdoc />
    public Task<Store?> FindAsync(Guid storeId, CancellationToken cancellationToken = default)
        => context.Stores.FirstOrDefaultAsync(store => store.Id == storeId, cancellationToken);

    /// <inheritdoc />
    public Task<Store?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalized = code.Trim().ToUpperInvariant();

        return context.Stores.FirstOrDefaultAsync(store => store.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public void Add(Store store) => context.Stores.Add(store);
}
