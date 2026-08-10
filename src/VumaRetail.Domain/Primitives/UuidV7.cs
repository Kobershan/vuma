namespace VumaRetail.Domain.Primitives;

/// <summary>
/// Generates RFC 9562 UUID version 7 identifiers — 48 bits of Unix millisecond timestamp followed by
/// random data, so values sort in creation order.
/// </summary>
/// <remarks>
/// <para>
/// ADR-004. Every primary key in Vuma is a UUID v7, generated client-side. That matters twice over:
/// an offline terminal can create records with no round-trip and no collision risk (R1), and the
/// time-ordering keeps B-tree index locality, which UUID v4 destroys.
/// </para>
/// <para>
/// Ordering is guaranteed across generations in the same millisecond by a counter seeded into bits
/// 48-59 (the <c>rand_a</c> field), per RFC 9562 §6.2 method 1. Without that, two IDs created in the
/// same millisecond would sort arbitrarily and an append-only ledger would lose its natural order.
/// </para>
/// </remarks>
public static class UuidV7
{
    private const int MaxCounter = 0x0FFF;

    private static readonly Lock Gate = new();
    private static long _lastTimestamp = -1;
    private static int _counter;

    /// <summary>Creates a new UUID v7 stamped with the current UTC time.</summary>
    public static Guid NewGuid() => NewGuid(DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates a new UUID v7 stamped with the supplied time. Exposed for deterministic tests; production
    /// code should use <see cref="NewGuid()"/>.
    /// </summary>
    /// <param name="timestamp">The instant to encode. Converted to Unix milliseconds.</param>
    public static Guid NewGuid(DateTimeOffset timestamp)
    {
        long unixMs = timestamp.ToUnixTimeMilliseconds();
        int counter;

        lock (Gate)
        {
            if (unixMs > _lastTimestamp)
            {
                _lastTimestamp = unixMs;
                _counter = 0;
            }
            else
            {
                // Same millisecond, or a clock that went backwards. Either way we keep issuing from the
                // last timestamp we used so IDs never go backwards — a backwards system clock must not
                // be able to produce an out-of-order key.
                unixMs = _lastTimestamp;

                if (_counter >= MaxCounter)
                {
                    // 4096 IDs inside one millisecond. Roll into the next millisecond rather than
                    // wrapping the counter, which would collide with IDs already issued.
                    _lastTimestamp = unixMs + 1;
                    unixMs = _lastTimestamp;
                    _counter = 0;
                }
                else
                {
                    _counter++;
                }
            }

            counter = _counter;
        }

        Span<byte> bytes = stackalloc byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes[8..]);

        // Bytes 0-5: big-endian 48-bit Unix millisecond timestamp.
        bytes[0] = (byte)(unixMs >> 40);
        bytes[1] = (byte)(unixMs >> 32);
        bytes[2] = (byte)(unixMs >> 24);
        bytes[3] = (byte)(unixMs >> 16);
        bytes[4] = (byte)(unixMs >> 8);
        bytes[5] = (byte)unixMs;

        // Bytes 6-7: version 7 in the high nibble, then the 12-bit monotonic counter.
        bytes[6] = (byte)(0x70 | ((counter >> 8) & 0x0F));
        bytes[7] = (byte)(counter & 0xFF);

        // Byte 8: RFC 9562 variant (10xxxxxx).
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>
    /// Reads the timestamp back out of a UUID v7. Useful for diagnostics and for partitioning ledgers
    /// by period without a separate column.
    /// </summary>
    /// <param name="id">A UUID v7 value.</param>
    /// <exception cref="ArgumentException">The value is not version 7.</exception>
    public static DateTimeOffset GetTimestamp(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!id.TryWriteBytes(bytes, bigEndian: true, out _))
        {
            throw new ArgumentException("Could not read the identifier's bytes.", nameof(id));
        }

        if ((bytes[6] & 0xF0) != 0x70)
        {
            throw new ArgumentException("The identifier is not a UUID version 7 value.", nameof(id));
        }

        long unixMs = ((long)bytes[0] << 40)
            | ((long)bytes[1] << 32)
            | ((long)bytes[2] << 24)
            | ((long)bytes[3] << 16)
            | ((long)bytes[4] << 8)
            | bytes[5];

        return DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
    }
}
