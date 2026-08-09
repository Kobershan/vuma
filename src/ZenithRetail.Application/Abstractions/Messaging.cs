namespace ZenithRetail.Application.Abstractions;

/// <summary>
/// A request that changes state. Every write in Zenith goes through one of these (CLAUDE.md §7 rule 2)
/// — there is no <c>SaveChanges</c> from an endpoint.
/// </summary>
/// <typeparam name="TResult">What the command returns to its caller.</typeparam>
/// <remarks>
/// Every implementation must carry a <see cref="CommandSideEffectAttribute"/>. See
/// <see cref="SideEffect"/> for why that is not optional.
/// </remarks>
public interface ICommand<TResult>;

/// <summary>A command that returns nothing but its success.</summary>
public interface ICommand : ICommand<Unit>;

/// <summary>A request that reads state and changes nothing.</summary>
/// <typeparam name="TResult">The data being asked for.</typeparam>
public interface IQuery<TResult>;

/// <summary>Handles one <see cref="ICommand{TResult}"/>.</summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">What the command returns.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>Handles one <see cref="IQuery{TResult}"/>.</summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResult">The data being returned.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    /// <summary>Executes the query.</summary>
    /// <param name="query">The query to execute.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Dispatches commands and queries to their handlers through the pipeline
/// (validation → transaction → audit → outbox → logging). ADR-009.
/// </summary>
public interface IDispatcher
{
    /// <summary>Sends a command through the pipeline to its handler.</summary>
    /// <typeparam name="TResult">What the command returns.</typeparam>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    /// <summary>Sends a query through the pipeline to its handler.</summary>
    /// <typeparam name="TResult">The data being asked for.</typeparam>
    /// <param name="query">The query to send.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}

/// <summary>The absence of a return value, for commands whose result is simply that they succeeded.</summary>
public readonly record struct Unit
{
    /// <summary>The single value.</summary>
    public static Unit Value => default;
}
