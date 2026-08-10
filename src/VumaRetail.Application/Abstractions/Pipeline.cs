using System.Reflection;

namespace VumaRetail.Application.Abstractions;

/// <summary>Whether the message in flight changes state or only reads it.</summary>
public enum MessageKind
{
    /// <summary>An <see cref="ICommand{TResult}"/>.</summary>
    Command = 0,

    /// <summary>An <see cref="IQuery{TResult}"/>.</summary>
    Query = 1,
}

/// <summary>
/// What a pipeline behaviour is told about the message it is wrapping.
/// </summary>
/// <remarks>
/// Deliberately small. A behaviour that needs more than this is usually a handler wearing a
/// disguise. The <see cref="Effect"/> and <see cref="Exemption"/> fields exist so Stage 04b's
/// read-only interceptor can decide from the envelope alone, without reflecting over the message
/// on every request (ADR-028, ADR-034).
/// </remarks>
/// <param name="Message">The command or query instance.</param>
/// <param name="MessageType">Its runtime type.</param>
/// <param name="Kind">Whether it writes or reads.</param>
/// <param name="Effect">The declared side effect, for a command. <c>ReadOnly</c> for a query.</param>
/// <param name="Exemption">The ADR-028 carve-out this command claims, if any.</param>
public sealed record MessageEnvelope(
    object Message,
    Type MessageType,
    MessageKind Kind,
    SideEffect Effect,
    ReadOnlyExemption Exemption)
{
    /// <summary>The message's simple type name, for logs and error messages.</summary>
    public string Name => MessageType.Name;

    /// <summary>Builds an envelope for a command, reading its mandatory classification.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The envelope.</returns>
    /// <remarks>
    /// A missing attribute is <see cref="SideEffect.Unclassified"/> rather than a throw. The
    /// architecture test already fails the build on one (ADR-034), and a pipeline that also threw
    /// would turn a build-time certainty into a runtime surprise on a customer's server.
    /// </remarks>
    public static MessageEnvelope ForCommand(object command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Type type = command.GetType();
        CommandSideEffectAttribute? classification = type.GetCustomAttribute<CommandSideEffectAttribute>();

        return new MessageEnvelope(
            command,
            type,
            MessageKind.Command,
            classification?.Effect ?? SideEffect.Unclassified,
            classification?.Exemption ?? ReadOnlyExemption.None);
    }

    /// <summary>Builds an envelope for a query.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The envelope.</returns>
    public static MessageEnvelope ForQuery(object query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return new MessageEnvelope(
            query,
            query.GetType(),
            MessageKind.Query,
            SideEffect.ReadOnly,
            ReadOnlyExemption.None);
    }
}

/// <summary>
/// The numbered slots in the command pipeline, outermost first (ADR-009).
/// </summary>
/// <remarks>
/// <para>
/// The numbers are reserved before the behaviours that occupy them exist, and that is the point. Two
/// of the five slots belong to stages that have not been built yet, and both of those stages are
/// obliged to slot into a chain that forty modules already depend on. Numbering them here means
/// Stage 04 and Stage 04b each add one registration; leaving it to them means renumbering.
/// </para>
/// <para>
/// A lower number is further out, so it sees the message first and the result last.
/// </para>
/// </remarks>
public static class PipelineOrder
{
    /// <summary>Structured logging and timing. Outermost, so it sees every failure below it.</summary>
    public const int Logging = 0;

    /// <summary>
    /// Reserved for Stage 04b. Refuses writes while a tenant is read-only (ADR-028), before any
    /// work is done — a refusal that had to validate first would be a refusal that costs a database
    /// round trip per rejected sale.
    /// </summary>
    public const int ReadOnlyGuard = 50;

    /// <summary>FluentValidation, before the handler and before the transaction opens.</summary>
    public const int Validation = 100;

    /// <summary>The unit of work. Commands only.</summary>
    public const int Transaction = 200;

    /// <summary>
    /// Reserved for Stage 04. The outbox row must be written inside the transaction opened above,
    /// or a crash between the two loses the replication silently (ADR-006).
    /// </summary>
    public const int Outbox = 300;
}

/// <summary>
/// One step in the command pipeline, wrapping the rest of it.
/// </summary>
/// <remarks>
/// Non-generic over the message on purpose. A generic <c>IPipelineBehaviour&lt;TMessage, TResult&gt;</c>
/// is the conventional shape, but it forces the dispatcher to close a generic type per behaviour per
/// message type at runtime — reflection in the hot path of every sale. The envelope carries what a
/// behaviour actually needs, and the result type stays generic on the method.
/// </remarks>
public interface IPipelineBehaviour
{
    /// <summary>Where this behaviour sits in the chain. See <see cref="PipelineOrder"/>.</summary>
    int Order { get; }

    /// <summary>Runs this step, calling <paramref name="next"/> to continue down the chain.</summary>
    /// <typeparam name="TResult">What the message returns.</typeparam>
    /// <param name="envelope">The message in flight.</param>
    /// <param name="next">The rest of the pipeline, ending at the handler.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<TResult> HandleAsync<TResult>(
        MessageEnvelope envelope,
        Func<CancellationToken, Task<TResult>> next,
        CancellationToken cancellationToken);
}
