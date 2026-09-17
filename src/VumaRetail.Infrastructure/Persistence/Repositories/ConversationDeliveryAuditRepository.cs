using VumaRetail.Application.Conversations;
using VumaRetail.Domain.Conversations;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>Persists every outbound conversation transport attempt in the company database.</summary>
public sealed class ConversationDeliveryAuditRepository(VumaRetailDbContext db) : IConversationDeliveryAudit
{
    public async Task RecordAsync(ConversationDeliveryAttempt attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        db.ConversationDeliveryAttempts.Add(attempt);
        await db.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
