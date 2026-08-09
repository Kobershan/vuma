namespace ZenithRetail.Application.Abstractions;

/// <summary>
/// The transaction boundary. The Stage 03 command pipeline opens and commits it; handlers do not.
/// </summary>
/// <remarks>
/// <para>
/// <c>CLAUDE.md</c> §7 rule 2 — every write goes through a command handler, and no host project calls
/// <c>SaveChanges</c>. This port is the reason that rule is affordable: a handler mutates tracked
/// entities and returns, and the pipeline commits once around it. An architecture test fails the
/// build if <c>SaveChanges</c> is called anywhere outside the persistence layer.
/// </para>
/// <para>
/// The single commit point also matters for ADR-006: the outbox row a change produces must land in
/// the same transaction as the change itself, or a crash between the two loses the replication and
/// the store silently diverges from the cloud. Stage 04 hangs the outbox write on this boundary.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Commits everything tracked since the last commit, as one transaction.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of rows written.</returns>
    Task<int> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside an explicit database transaction, committing on
    /// success and rolling back on any exception.
    /// </summary>
    /// <typeparam name="TResult">What the operation returns.</typeparam>
    /// <param name="operation">The work to run transactionally.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
