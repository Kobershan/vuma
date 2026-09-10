using VumaRetail.Domain.Conversations;

namespace VumaRetail.Application.Conversations;

/// <summary>Manages the customer-account/company scope granted to a channel binding.</summary>
public interface IConversationScopeManagementService
{
    /// <summary>Adds one reachable account/company pair to a binding.</summary>
    Task<ConversationAccountScope> AddAsync(ConversationAccountScope scope, CancellationToken cancellationToken = default);

    /// <summary>Lists the scopes granted to a binding.</summary>
    Task<IReadOnlyList<ConversationAccountScope>> ListAsync(Guid bindingId, CancellationToken cancellationToken = default);
}

/// <summary>Reads the structural account/company scope for a conversation binding.</summary>
public interface IConversationScopeReader
{
    /// <summary>Returns all account/company pairs granted to the binding.</summary>
    Task<IReadOnlyList<ConversationAccountScope>> ListAsync(Guid bindingId, CancellationToken cancellationToken = default);
}
