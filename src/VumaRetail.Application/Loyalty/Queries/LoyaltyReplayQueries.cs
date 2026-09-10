using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Loyalty;

namespace VumaRetail.Application.Loyalty.Queries;

/// <summary>What the idempotency log holds for one key (Stage 20).</summary>
/// <param name="TransactionId">The transaction.</param>
/// <param name="IsTerminal">True for confirmed, failed or refused outcomes.</param>
public sealed record TransactionReplayEntry(Guid TransactionId, bool IsTerminal);

/// <summary>Reads the idempotency log for one key. The rate-limit replay exemption's source.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Key">The idempotency key.</param>
public sealed record GetTransactionByKeyQuery(Guid CompanyId, Guid Key)
    : IQuery<TransactionReplayEntry?>;

/// <summary>Reads the log.</summary>
/// <param name="transactions">Transaction persistence.</param>
public sealed class GetTransactionByKeyQueryHandler(ILoyaltyTransactionRepository transactions)
    : IQueryHandler<GetTransactionByKeyQuery, TransactionReplayEntry?>
{
    /// <inheritdoc />
    public async Task<TransactionReplayEntry?> HandleAsync(
        GetTransactionByKeyQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        LoyaltyTransaction? row = await transactions
            .FindByKeyAsync(query.CompanyId, query.Key, cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? null
            : new TransactionReplayEntry(
                row.Id,
                row.Status is TransactionStatus.Confirmed
                    or TransactionStatus.Failed);
    }
}
