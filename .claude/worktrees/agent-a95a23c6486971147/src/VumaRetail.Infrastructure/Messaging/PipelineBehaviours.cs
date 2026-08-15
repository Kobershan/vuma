using System.Diagnostics;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Infrastructure.Messaging;

/// <summary>
/// One structured log line per message, with its outcome, its duration and the request's
/// correlation id.
/// </summary>
/// <remarks>
/// <para>
/// Outermost in the chain, so it sees a validation refusal and a read-only refusal as well as a
/// handler failure. That is the point: "the till said no" is a support call, and the log has to be
/// able to answer which of the four possible noes it was.
/// </para>
/// <para>
/// <b>The message itself is never logged</b> — only its type name. A <c>CreateUserCommand</c> holds
/// a password and a <c>SetUserPinCommand</c> holds a PIN, and a pipeline that logged its payload
/// would put both in a file on a shop's back-office PC. Redaction in the sink is the safety net
/// (<c>docs/SECURITY.md</c> §4); not logging them here is the actual control.
/// </para>
/// </remarks>
/// <param name="logger">Where the lines go.</param>
/// <param name="correlation">The request's correlation id.</param>
public sealed class LoggingBehaviour(ILogger<LoggingBehaviour> logger, ICorrelationContext correlation)
    : IPipelineBehaviour
{
    /// <inheritdoc />
    public int Order => PipelineOrder.Logging;

    /// <inheritdoc />
    public async Task<TResult> HandleAsync<TResult>(
        MessageEnvelope envelope,
        Func<CancellationToken, Task<TResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(next);

        long started = Stopwatch.GetTimestamp();

        using IDisposable? scope = logger.BeginScope(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["CorrelationId"] = correlation.CorrelationId,
            ["Message"] = envelope.Name,
            ["MessageKind"] = envelope.Kind.ToString(),
        });

        try
        {
            TResult result = await next(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "{Message} handled in {ElapsedMilliseconds} ms",
                envelope.Name,
                Elapsed(started));

            return result;
        }
        catch (DomainException refused)
        {
            // Expected. A rule said no, which is information rather than a fault, so it is a warning
            // with the code and not an error with a stack trace.
            logger.LogWarning(
                "{Message} refused with {ErrorCode} after {ElapsedMilliseconds} ms",
                envelope.Name,
                refused.Code,
                Elapsed(started));

            throw;
        }
        catch (Exception failure)
        {
            logger.LogError(
                failure,
                "{Message} failed after {ElapsedMilliseconds} ms",
                envelope.Name,
                Elapsed(started));

            throw;
        }
    }

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}

/// <summary>
/// Runs the FluentValidation validators registered for the message, before the handler and before
/// the transaction opens.
/// </summary>
/// <remarks>
/// <para>
/// Validators are resolved for the message's runtime type, which is why this behaviour takes the
/// container rather than a typed dependency: the dispatcher knows <c>TResult</c>, not
/// <c>TCommand</c>. Resolution happens once per message, not once per rule.
/// </para>
/// <para>
/// A message with no validator is valid. That is deliberate — most queries have nothing worth
/// validating, and requiring an empty validator per query would produce forty empty classes nobody
/// reads. Handlers keep their own guards regardless (<c>CONVENTIONS.md</c> §4): the validator is
/// the contract at the edge, the guard is what makes the rule true for every caller.
/// </para>
/// </remarks>
/// <param name="services">Resolves <c>IValidator&lt;TMessage&gt;</c> for the runtime message type.</param>
public sealed class ValidationBehaviour(IServiceProvider services) : IPipelineBehaviour
{
    /// <inheritdoc />
    public int Order => PipelineOrder.Validation;

    /// <inheritdoc />
    public async Task<TResult> HandleAsync<TResult>(
        MessageEnvelope envelope,
        Func<CancellationToken, Task<TResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(next);

        Type validatorType = typeof(IValidator<>).MakeGenericType(envelope.MessageType);
        IValidator[] validators = [.. services.GetServices(validatorType).OfType<IValidator>()];

        if (validators.Length > 0)
        {
            IValidationContext context = new ValidationContext<object>(envelope.Message);
            List<ValidationFailure> failures = [];

            foreach (IValidator validator in validators)
            {
                ValidationResult result = await validator
                    .ValidateAsync(context, cancellationToken)
                    .ConfigureAwait(false);

                failures.AddRange(result.Errors.Where(failure => failure is not null));
            }

            if (failures.Count > 0)
            {
                throw new ValidationFailedException(
                    envelope.Name,
                    failures
                        .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
                        .ToDictionary(
                            group => group.Key,
                            group => group.Select(failure => failure.ErrorMessage).ToArray(),
                            StringComparer.Ordinal));
            }
        }

        return await next(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Opens the transaction for a command and commits it once, after the handler returns.
/// </summary>
/// <remarks>
/// <para>
/// <c>CLAUDE.md</c> §7 rule 2 and ADR-006. A handler that commits for itself makes a half-applied
/// command possible the moment a command does two things, and it makes the outbox unimplementable:
/// the outbox row has to land in the same transaction as the change that produced it, and it cannot
/// if the change has already been committed by the time the outbox behaviour is reached.
/// </para>
/// <para>
/// <b>Queries never open a transaction.</b> ADR-028 promises that reporting keeps working while a
/// tenant is read-only, and a report that takes a write transaction is how a slow query becomes a
/// store-wide lock on a Saturday.
/// </para>
/// </remarks>
/// <param name="unitOfWork">The transaction boundary.</param>
public sealed class TransactionBehaviour(IUnitOfWork unitOfWork) : IPipelineBehaviour
{
    /// <inheritdoc />
    public int Order => PipelineOrder.Transaction;

    /// <inheritdoc />
    public async Task<TResult> HandleAsync<TResult>(
        MessageEnvelope envelope,
        Func<CancellationToken, Task<TResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(next);

        if (envelope.Kind != MessageKind.Command)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        // ExecuteInTransactionAsync saves and commits on success and rolls back on any exception,
        // and no-ops into an outer transaction if one is already open — so a command dispatched from
        // inside another command joins its parent's boundary rather than committing half of it.
        return await unitOfWork
            .ExecuteInTransactionAsync(next, cancellationToken)
            .ConfigureAwait(false);
    }
}
