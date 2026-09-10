using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Conversations;
using VumaRetail.Domain.Conversations;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Conversations;

/// <summary>Persists explicit account/company boundaries for conversation bindings.</summary>
public sealed class ConversationScopeManagementService(VumaRegistryDbContext registry) : IConversationScopeManagementService
{
    public async Task<ConversationAccountScope> AddAsync(ConversationAccountScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        bool bindingExists = await registry.ContactBindings.AnyAsync(x => x.Id == scope.BindingId, cancellationToken).ConfigureAwait(false);
        if (!bindingExists)
        {
            throw new InvalidOperationException("Conversation binding not found.");
        }

        registry.ConversationAccountScopes.Add(scope);
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return scope;
    }

    public async Task<IReadOnlyList<ConversationAccountScope>> ListAsync(Guid bindingId, CancellationToken cancellationToken = default)
    {
        if (bindingId == Guid.Empty)
        {
            throw new ArgumentException("A binding is required.", nameof(bindingId));
        }

        return await registry.ConversationAccountScopes
            .AsNoTracking()
            .Where(x => x.BindingId == bindingId)
            .OrderBy(x => x.OperatingCompanyId)
            .ThenBy(x => x.CustomerAccountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
