using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Conversations;
using VumaRetail.Domain.Conversations;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>Durable conversation state and append-only transcript storage.</summary>
public sealed class EfConversationStore(VumaRetailDbContext db) : IConversationStore
{
    public async Task<Conversation> GetOrCreateAsync(ContactBinding binding, ConversationChannel channel, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        Conversation? conversation = await db.Conversations
            .OrderByDescending(x => x.LastActivityAt)
            .FirstOrDefaultAsync(x => x.ContactBindingId == binding.Id && x.Channel == channel && x.State != ConversationState.Done, cancellationToken)
            .ConfigureAwait(false);
        if (conversation is not null) return conversation;

        conversation = new Conversation(binding.TenantId, binding.Id, channel, at);
        db.Conversations.Add(conversation);
        await db.CommitAsync(cancellationToken).ConfigureAwait(false);
        return conversation;
    }

    public async Task AddTurnAsync(ConversationTurn turn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);
        db.ConversationTurns.Add(turn);
        await db.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<ConversationTurn?> FindTurnByExternalMessageIdAsync(Guid conversationId, string externalMessageId, CancellationToken cancellationToken = default)
        => db.ConversationTurns.FirstOrDefaultAsync(x => x.ConversationId == conversationId && x.ExternalMessageId == externalMessageId.Trim(), cancellationToken);

    public async Task<IReadOnlyList<ConversationTurn>> ListTurnsAsync(Guid conversationId, CancellationToken cancellationToken = default)
        => await db.ConversationTurns.AsNoTracking().Where(x => x.ConversationId == conversationId)
            .OrderBy(x => x.HappenedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
}
