using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;

namespace VumaRetail.Infrastructure.Messaging;

/// <summary>
/// The CQRS dispatcher ADR-009 describes: resolve the handler, run it through the ordered pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Hand-rolled rather than MediatR — about two hundred lines we own, with no third-party licensing
/// exposure and a pipeline that can be read top to bottom. See <see cref="PipelineOrder"/> for the
/// chain and for the two slots later stages fill.
/// </para>
/// <para>
/// The awkward part of any dispatcher is that <c>SendAsync</c> knows <c>TResult</c> but not
/// <c>TCommand</c>, while <see cref="ICommandHandler{TCommand,TResult}"/> needs both. The usual
/// answer is <c>MethodInfo.Invoke</c> on every call, which allocates, defeats the JIT and turns
/// every handler exception into a <c>TargetInvocationException</c> somebody has to unwrap. Instead
/// each command type gets a closed generic executor built once and cached: one reflection call per
/// command type per process, and an ordinary virtual call thereafter.
/// </para>
/// </remarks>
/// <param name="services">Resolves handlers. Scoped, so a handler sees the request's DbContext.</param>
/// <param name="behaviours">The pipeline, in any order — this type sorts them.</param>
public sealed class Dispatcher(IServiceProvider services, IEnumerable<IPipelineBehaviour> behaviours) : IDispatcher
{
    private readonly IPipelineBehaviour[] _behaviours =
        [.. (behaviours ?? throw new ArgumentNullException(nameof(behaviours))).OrderBy(behaviour => behaviour.Order)];

    /// <inheritdoc />
    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        MessageEnvelope envelope = MessageEnvelope.ForCommand(command);

        return RunAsync(
            envelope,
            token => CommandExecutor<TResult>.For(envelope.MessageType).ExecuteAsync(services, command, token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        MessageEnvelope envelope = MessageEnvelope.ForQuery(query);

        return RunAsync(
            envelope,
            token => QueryExecutor<TResult>.For(envelope.MessageType).ExecuteAsync(services, query, token),
            cancellationToken);
    }

    private Task<TResult> RunAsync<TResult>(
        MessageEnvelope envelope,
        Func<CancellationToken, Task<TResult>> handler,
        CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<TResult>> next = handler;

        // Built inside-out: the last behaviour wrapped is the one that runs first, so walking the
        // sorted list backwards puts the lowest Order outermost.
        for (int index = _behaviours.Length - 1; index >= 0; index--)
        {
            IPipelineBehaviour behaviour = _behaviours[index];
            Func<CancellationToken, Task<TResult>> inner = next;

            next = token => behaviour.HandleAsync(envelope, inner, token);
        }

        return next(cancellationToken);
    }

    private static InvalidOperationException NoHandler(Type messageType, string kind) => new(
        $"No {kind} handler is registered for {messageType.FullName}. Handlers are discovered by the "
        + "Scrutor scan in AddVumaMessaging; a handler in an assembly that scan does not cover has to "
        + "be registered by that module's composition root.");

    private abstract class CommandExecutor<TResult>
    {
        private static readonly ConcurrentDictionary<Type, CommandExecutor<TResult>> Cache = new();

        public static CommandExecutor<TResult> For(Type commandType) => Cache.GetOrAdd(
            commandType,
            static type => (CommandExecutor<TResult>)Activator.CreateInstance(
                typeof(TypedCommandExecutor<,>).MakeGenericType(type, typeof(TResult)))!);

        public abstract Task<TResult> ExecuteAsync(
            IServiceProvider services,
            object command,
            CancellationToken cancellationToken);
    }

    private sealed class TypedCommandExecutor<TCommand, TResult> : CommandExecutor<TResult>
        where TCommand : ICommand<TResult>
    {
        public override Task<TResult> ExecuteAsync(
            IServiceProvider services,
            object command,
            CancellationToken cancellationToken)
        {
            ICommandHandler<TCommand, TResult> handler =
                services.GetService<ICommandHandler<TCommand, TResult>>()
                ?? throw NoHandler(typeof(TCommand), "command");

            return handler.HandleAsync((TCommand)command, cancellationToken);
        }
    }

    private abstract class QueryExecutor<TResult>
    {
        private static readonly ConcurrentDictionary<Type, QueryExecutor<TResult>> Cache = new();

        public static QueryExecutor<TResult> For(Type queryType) => Cache.GetOrAdd(
            queryType,
            static type => (QueryExecutor<TResult>)Activator.CreateInstance(
                typeof(TypedQueryExecutor<,>).MakeGenericType(type, typeof(TResult)))!);

        public abstract Task<TResult> ExecuteAsync(
            IServiceProvider services,
            object query,
            CancellationToken cancellationToken);
    }

    private sealed class TypedQueryExecutor<TQuery, TResult> : QueryExecutor<TResult>
        where TQuery : IQuery<TResult>
    {
        public override Task<TResult> ExecuteAsync(
            IServiceProvider services,
            object query,
            CancellationToken cancellationToken)
        {
            IQueryHandler<TQuery, TResult> handler =
                services.GetService<IQueryHandler<TQuery, TResult>>()
                ?? throw NoHandler(typeof(TQuery), "query");

            return handler.HandleAsync((TQuery)query, cancellationToken);
        }
    }
}
