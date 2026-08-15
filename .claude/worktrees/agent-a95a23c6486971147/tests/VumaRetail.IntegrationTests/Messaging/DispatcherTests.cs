using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Infrastructure.DependencyInjection;

namespace VumaRetail.IntegrationTests.Messaging;

/// <summary>
/// The Stage 03 dispatcher and its pipeline (ADR-009).
/// </summary>
/// <remarks>
/// No database. These assert the shape of the pipeline — what runs, in what order, and who commits —
/// which is exactly the part a real database would hide behind a working query.
/// </remarks>
public sealed class DispatcherTests
{
    [Fact]
    public async Task Resolves_a_handler_from_the_commands_runtime_type()
    {
        await using Harness harness = Harness.Build();

        string result = await harness.Dispatcher.SendAsync(new EchoCommand("shelf-talker"));

        result.Should().Be("shelf-talker");
    }

    [Fact]
    public async Task Resolves_a_query_handler_too()
    {
        await using Harness harness = Harness.Build();

        int result = await harness.Dispatcher.QueryAsync(new CountQuery(7));

        result.Should().Be(7);
    }

    [Fact]
    public async Task Names_the_message_type_when_nothing_handles_it()
    {
        await using Harness harness = Harness.Build(registerHandlers: false);

        Func<Task> send = () => harness.Dispatcher.SendAsync(new EchoCommand("orphan"));

        (await send.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain(nameof(EchoCommand));
    }

    [Fact]
    public async Task Runs_the_reserved_slots_in_the_order_two_later_stages_need()
    {
        // This is the contract Stage 04 and Stage 04b are being asked to build against, asserted
        // before either exists. The read-only guard must refuse a write *before* the transaction
        // opens, or every refused sale costs a database round trip; the outbox must write *inside*
        // it, or ADR-006's exactly-once effect is lost to any crash between the two.
        await using Harness harness = Harness.Build();

        await harness.Dispatcher.SendAsync(new EchoCommand("order"));

        harness.Trace.Should().ContainInOrder("read-only-guard", "transaction", "outbox", "handler");
    }

    [Fact]
    public async Task Refuses_an_invalid_command_before_the_handler_runs()
    {
        await using Harness harness = Harness.Build();

        Func<Task> send = () => harness.Dispatcher.SendAsync(new EchoCommand(""));

        (await send.Should().ThrowAsync<ValidationFailedException>())
            .Which.Code.Should().Be(ValidationFailedException.ErrorCode);

        harness.Trace.Should().NotContain("handler");
    }

    [Fact]
    public async Task Groups_validation_failures_by_the_property_that_produced_them()
    {
        await using Harness harness = Harness.Build();

        Func<Task> send = () => harness.Dispatcher.SendAsync(new EchoCommand(""));

        ValidationFailedException failure = (await send.Should().ThrowAsync<ValidationFailedException>()).Which;

        failure.Errors.Should().ContainKey(nameof(EchoCommand.Value));
        failure.Errors[nameof(EchoCommand.Value)].Should().NotBeEmpty();
    }

    [Fact]
    public async Task Commits_a_command_once_through_the_unit_of_work()
    {
        // ADR-044: the pipeline owns the transaction. A handler that committed for itself would make
        // this two, and ADR-006's outbox row could then land outside the change that produced it.
        await using Harness harness = Harness.Build();

        await harness.Dispatcher.SendAsync(new EchoCommand("once"));

        harness.UnitOfWork.Transactions.Should().Be(1);
        harness.UnitOfWork.Commits.Should().Be(0);
    }

    [Fact]
    public async Task Never_opens_a_transaction_for_a_query()
    {
        // ADR-028 promises reporting keeps working while a tenant is read-only. A report that takes a
        // write transaction is how a slow query becomes a store-wide lock on a Saturday.
        await using Harness harness = Harness.Build();

        await harness.Dispatcher.QueryAsync(new CountQuery(3));

        harness.UnitOfWork.Transactions.Should().Be(0);
    }

    [Fact]
    public async Task Rolls_back_when_the_handler_throws()
    {
        await using Harness harness = Harness.Build();

        Func<Task> send = () => harness.Dispatcher.SendAsync(new EchoCommand("boom"));

        await send.Should().ThrowAsync<InvalidOperationException>();
        harness.UnitOfWork.RolledBack.Should().BeTrue();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private ServiceProvider _services = null!;

        public IDispatcher Dispatcher { get; private set; } = null!;

        public RecordingUnitOfWork UnitOfWork { get; } = new();

        public List<string> Trace { get; } = [];

        public static Harness Build(bool registerHandlers = true)
        {
            Harness harness = new();

            ServiceCollection services = new();
            services.AddLogging();
            services.AddSingleton(harness.Trace);
            services.AddSingleton<IUnitOfWork>(harness.UnitOfWork);
            services.AddVumaMessaging(typeof(DispatcherTests).Assembly);

            // Stand-ins for the two slots PipelineOrder reserves and this stage leaves empty.
            services.AddSingleton<IPipelineBehaviour>(
                new ProbeBehaviour(PipelineOrder.ReadOnlyGuard, "read-only-guard", harness.Trace));
            services.AddSingleton<IPipelineBehaviour>(
                new ProbeBehaviour(PipelineOrder.Outbox, "outbox", harness.Trace));

            if (!registerHandlers)
            {
                foreach (ServiceDescriptor descriptor in services
                    .Where(descriptor => descriptor.ServiceType.IsGenericType
                        && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
                    .ToList())
                {
                    services.Remove(descriptor);
                }
            }

            harness.UnitOfWork.Trace = harness.Trace;
            harness._services = services.BuildServiceProvider();
            harness.Dispatcher = harness._services.GetRequiredService<IDispatcher>();

            return harness;
        }

        public async ValueTask DisposeAsync() => await _services.DisposeAsync();
    }

    private sealed class ProbeBehaviour(int order, string name, List<string> trace) : IPipelineBehaviour
    {
        public int Order => order;

        public Task<TResult> HandleAsync<TResult>(
            MessageEnvelope envelope,
            Func<CancellationToken, Task<TResult>> next,
            CancellationToken cancellationToken)
        {
            trace.Add(name);

            return next(cancellationToken);
        }
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public List<string> Trace { get; set; } = [];

        public int Commits { get; private set; }

        public int Transactions { get; private set; }

        public bool RolledBack { get; private set; }

        public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        {
            Commits++;
            return Task.FromResult(0);
        }

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            Transactions++;
            Trace.Add("transaction");

            try
            {
                return await operation(cancellationToken);
            }
            catch
            {
                RolledBack = true;
                throw;
            }
        }
    }
}

/// <summary>A command that returns what it was given, so the pipeline is the only thing under test.</summary>
/// <param name="Value">Anything. <c>boom</c> makes the handler throw.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record EchoCommand(string Value) : ICommand<string>;

/// <summary>Refuses an empty value, so the validation behaviour has something to refuse.</summary>
public sealed class EchoCommandValidator : AbstractValidator<EchoCommand>
{
    /// <summary>Builds the rules.</summary>
    public EchoCommandValidator() => RuleFor(command => command.Value).NotEmpty();
}

/// <summary>Handles <see cref="EchoCommand"/>.</summary>
/// <param name="trace">Records that the handler was reached.</param>
public sealed class EchoCommandHandler(List<string> trace) : ICommandHandler<EchoCommand, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(EchoCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        trace.Add("handler");

        return command.Value == "boom"
            ? throw new InvalidOperationException("boom")
            : Task.FromResult(command.Value);
    }
}

/// <summary>A query that returns what it was given.</summary>
/// <param name="Value">The number to return.</param>
public sealed record CountQuery(int Value) : IQuery<int>;

/// <summary>Handles <see cref="CountQuery"/>.</summary>
public sealed class CountQueryHandler : IQueryHandler<CountQuery, int>
{
    /// <inheritdoc />
    public Task<int> HandleAsync(CountQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult(query.Value);
    }
}
