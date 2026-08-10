using System.Globalization;

namespace VumaRetail.Domain.Primitives;

/// <summary>
/// A hybrid logical clock reading — the ordering device ADR-007 puts on every replicated change.
/// </summary>
/// <remarks>
/// <para>
/// Three fields, compared in order. <see cref="PhysicalMilliseconds"/> is the node's wall clock, so
/// stamps stay roughly meaningful to a human reading a log. <see cref="Counter"/> breaks ties inside
/// the same millisecond and, more importantly, keeps the clock moving forward when the wall clock
/// does not — a store server whose time is corrected backwards by NTP must not start re-issuing
/// stamps it has already used. <see cref="NodeId"/> breaks the remaining ties, so the order is
/// <em>total</em> rather than merely partial: two nodes that independently stamp the same
/// millisecond and the same counter still agree on which came first, and both reach that answer
/// without talking to each other.
/// </para>
/// <para>
/// That last property is the whole point. A store and the cloud cannot share a clock, and a
/// last-writer-wins rule over a partial order is a rule that resolves differently depending on which
/// node evaluates it — which is a system that converges on two different answers and calls itself
/// converged.
/// </para>
/// <para>
/// The stamp is not a timestamp and no business rule may read it as one. It orders events; when
/// something happened is <c>occurred_at</c>, from <c>IClock</c>.
/// </para>
/// </remarks>
/// <param name="PhysicalMilliseconds">Unix epoch milliseconds, UTC, from the issuing node's clock.</param>
/// <param name="Counter">Logical counter within the millisecond, and the carry when the clock stalls.</param>
/// <param name="NodeId">The node that issued the stamp. The final tie-break, so the order is total.</param>
public readonly record struct HlcStamp(long PhysicalMilliseconds, int Counter, string NodeId)
    : IComparable<HlcStamp>
{
    /// <summary>The widest a node id may be. Matches the column and the string form's fixed layout.</summary>
    public const int NodeIdMaxLength = 64;

    /// <summary>A stamp that precedes every issued one. The starting cursor for a peer never heard from.</summary>
    public static HlcStamp MinValue => new(0, 0, string.Empty);

    /// <summary>True when this is <see cref="MinValue"/> — no stamp rather than an early one.</summary>
    public bool IsEmpty => PhysicalMilliseconds == 0 && Counter == 0 && NodeId.Length == 0;

    /// <summary>
    /// The sortable string form, <c>{physical:D14}:{counter:D6}:{node}</c>.
    /// </summary>
    /// <remarks>
    /// Zero-padded so an ordinal string sort produces the same order as <see cref="CompareTo"/>. That
    /// is what lets a cursor be a single indexed column and a batch be ordered by the database
    /// instead of in memory — and it is asserted by a test, because the day the padding is dropped
    /// nothing fails loudly, sync just starts skipping rows.
    /// </remarks>
    /// <returns>The sortable string form.</returns>
    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{PhysicalMilliseconds:D14}:{Counter:D6}:{NodeId}");

    /// <summary>Parses the string form produced by <see cref="ToString"/>.</summary>
    /// <param name="value">The stamp in <c>{physical}:{counter}:{node}</c> form.</param>
    /// <returns>The parsed stamp.</returns>
    /// <exception cref="MalformedHlcStampException">The string is not a stamp.</exception>
    public static HlcStamp Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length == 0)
        {
            return MinValue;
        }

        int firstSeparator = value.IndexOf(':', StringComparison.Ordinal);
        int secondSeparator = firstSeparator < 0
            ? -1
            : value.IndexOf(':', firstSeparator + 1);

        if (firstSeparator < 0 || secondSeparator < 0)
        {
            throw new MalformedHlcStampException(value);
        }

        if (!long.TryParse(
                value.AsSpan(0, firstSeparator),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long physical)
            || !int.TryParse(
                value.AsSpan(firstSeparator + 1, secondSeparator - firstSeparator - 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int counter))
        {
            throw new MalformedHlcStampException(value);
        }

        string node = value[(secondSeparator + 1)..];

        return node.Length > NodeIdMaxLength ? throw new MalformedHlcStampException(value) : new HlcStamp(physical, counter, node);
    }

    /// <summary>Orders two stamps: physical, then counter, then node id.</summary>
    /// <param name="other">The stamp to compare against.</param>
    /// <returns>Negative, zero or positive in the usual way.</returns>
    public int CompareTo(HlcStamp other)
    {
        int physical = PhysicalMilliseconds.CompareTo(other.PhysicalMilliseconds);

        if (physical != 0)
        {
            return physical;
        }

        int counter = Counter.CompareTo(other.Counter);

        return counter != 0 ? counter : string.CompareOrdinal(NodeId, other.NodeId);
    }

    /// <summary>True when the left stamp orders before the right.</summary>
    /// <param name="left">The left stamp.</param>
    /// <param name="right">The right stamp.</param>
    public static bool operator <(HlcStamp left, HlcStamp right) => left.CompareTo(right) < 0;

    /// <summary>True when the left stamp orders after the right.</summary>
    /// <param name="left">The left stamp.</param>
    /// <param name="right">The right stamp.</param>
    public static bool operator >(HlcStamp left, HlcStamp right) => left.CompareTo(right) > 0;

    /// <summary>True when the left stamp orders at or before the right.</summary>
    /// <param name="left">The left stamp.</param>
    /// <param name="right">The right stamp.</param>
    public static bool operator <=(HlcStamp left, HlcStamp right) => left.CompareTo(right) <= 0;

    /// <summary>True when the left stamp orders at or after the right.</summary>
    /// <param name="left">The left stamp.</param>
    /// <param name="right">The right stamp.</param>
    public static bool operator >=(HlcStamp left, HlcStamp right) => left.CompareTo(right) >= 0;
}

/// <summary>Thrown when a string that should be an <see cref="HlcStamp"/> is not one.</summary>
/// <param name="value">The offending string.</param>
/// <remarks>
/// <see cref="DomainProblemKind.Malformed"/> rather than <c>Rule</c>: a peer that sends a stamp this
/// shape is a peer with a bug, and the answer it needs is "your request was wrong" rather than "the
/// business said no".
/// </remarks>
public sealed class MalformedHlcStampException(string value)
    : DomainException(
        "SYNC_STAMP_MALFORMED",
        $"'{value}' is not a hybrid logical clock stamp. The form is "
        + "{physicalMilliseconds}:{counter}:{nodeId} (ADR-007).",
        DomainProblemKind.Malformed);
