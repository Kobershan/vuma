namespace VumaRetail.Application.Abstractions;

/// <summary>
/// The identifier that ties one request's log lines, audit rows and error body together.
/// </summary>
/// <remarks>
/// <para>
/// A shop rings support and says "it said something went wrong at about half past four". The only
/// affordable answer to that is a code on the screen that finds the exact request in a log file, and
/// the only way to have one is to mint it at the edge and carry it everywhere. It is the entire
/// payload of a <c>500</c> — see <c>CONVENTIONS.md</c> §5 and <c>docs/SECURITY.md</c> §4 on why a
/// server error tells the caller nothing else.
/// </para>
/// <para>
/// Scoped per request. A client may supply one (so a desktop terminal's own log and the server's
/// agree); one is minted when it does not.
/// </para>
/// </remarks>
public interface ICorrelationContext
{
    /// <summary>The current request's correlation id. Never empty.</summary>
    string CorrelationId { get; }

    /// <summary>Adopts an id the caller supplied.</summary>
    /// <param name="correlationId">The id. Ignored when null or blank.</param>
    void Set(string? correlationId);
}
