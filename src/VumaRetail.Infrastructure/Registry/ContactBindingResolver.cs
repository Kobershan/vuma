using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Conversations;
using VumaRetail.Domain.Conversations;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>Resolves channel addresses from the tenant registry with no company data access.</summary>
public sealed class EfContactResolver(VumaRegistryDbContext registry) : IContactResolver
{
    public Task<ContactBinding?> ResolveAsync(ConversationChannel channel, string address, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        return registry.ContactBindings.SingleOrDefaultAsync(
            binding => binding.Channel == channel && binding.Address == address.Trim(), cancellationToken);
    }
}
