namespace VumaRetail.Application.Abstractions;

/// <summary>
/// The only source of time in Vuma. Nothing outside this port's implementation reads the wall clock.
/// </summary>
/// <remarks>
/// <para>
/// <c>CONVENTIONS.md</c> §6 bans <c>DateTime.Now</c> and <c>DateTimeOffset.UtcNow</c> outside
/// <c>SystemClock</c>, and an architecture test fails the build on either. The reason is not
/// tidiness: a licensing ladder that runs over 45 days (ADR-028), a lease that expires in 72 hours,
/// an accounting period close, a promotion window and a lay-by expiry all have to be tested at their
/// boundaries. Code that reads the wall clock can only be tested by waiting.
/// </para>
/// <para>
/// Everything returned is UTC (<c>CLAUDE.md</c> §7 rule 9). Local time is a presentation concern and
/// is applied at the edge, using the store's timezone, never in a domain rule.
/// </para>
/// </remarks>
public interface IClock
{
    /// <summary>The current instant, always UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
