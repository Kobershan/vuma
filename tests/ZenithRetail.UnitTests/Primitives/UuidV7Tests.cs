using ZenithRetail.Domain.Primitives;

namespace ZenithRetail.UnitTests.Primitives;

/// <summary>
/// ADR-004. The two properties that matter: the value is a real UUID v7, and a sequence of them sorts
/// in generation order even when generated faster than the clock ticks.
/// </summary>
public sealed class UuidV7Tests
{
    [Fact]
    public void Sets_version_7_and_the_RFC_9562_variant()
    {
        Guid id = UuidV7.NewGuid();
        byte[] bytes = id.ToByteArray(bigEndian: true);

        (bytes[6] >> 4).Should().Be(7, "the high nibble of byte 6 is the UUID version");
        (bytes[8] >> 6).Should().Be(0b10, "the top two bits of byte 8 are the RFC 9562 variant");
    }

    [Fact]
    public void Encodes_the_supplied_timestamp_and_reads_it_back()
    {
        var when = new DateTimeOffset(2026, 8, 9, 14, 30, 15, TimeSpan.Zero);

        Guid id = UuidV7.NewGuid(when);

        UuidV7.GetTimestamp(id).ToUnixTimeMilliseconds().Should().Be(when.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Ids_generated_in_a_tight_loop_sort_in_generation_order()
    {
        // This is the whole point of v7 over v4. Thousands of IDs land in the same millisecond during a
        // bulk import or a ledger rebuild; if they sorted arbitrarily, an append-only ledger would lose
        // its natural order and B-tree locality would go with it.
        Guid[] ids = [.. Enumerable.Range(0, 10_000).Select(_ => UuidV7.NewGuid())];

        Guid[] sorted = [.. ids.OrderBy(id => id.ToByteArray(bigEndian: true), ByteArrayComparer.Instance)];

        sorted.Should().Equal(ids, "UUID v7 values must sort in the order they were created");
    }

    [Fact]
    public void Ids_are_unique_across_a_large_batch()
    {
        Guid[] ids = [.. Enumerable.Range(0, 50_000).Select(_ => UuidV7.NewGuid())];

        ids.Distinct().Should().HaveCount(ids.Length);
    }

    [Fact]
    public void Ids_are_unique_and_ordered_when_generated_from_many_threads()
    {
        // Terminals generate IDs on whatever thread the till is on while a background sync loop is also
        // writing. The counter has to hold up under that or two rows collide.
        Guid[] ids = [.. Enumerable.Range(0, 20_000)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .Select(_ => UuidV7.NewGuid())];

        ids.Distinct().Should().HaveCount(ids.Length, "concurrent generation must not collide");
    }

    [Fact]
    public void A_backwards_clock_cannot_produce_an_out_of_order_id()
    {
        // A store server whose clock is corrected backwards by NTP, or a terminal with a flat CMOS
        // battery, must not start issuing keys that sort before rows already written.
        Guid before = UuidV7.NewGuid(DateTimeOffset.UtcNow);
        Guid after = UuidV7.NewGuid(DateTimeOffset.UtcNow.AddHours(-3));

        ByteArrayComparer.Instance
            .Compare(after.ToByteArray(bigEndian: true), before.ToByteArray(bigEndian: true))
            .Should().BeGreaterThan(0, "a clock going backwards must not move identities backwards");
    }

    [Fact]
    public void Rejects_a_value_that_is_not_version_7()
    {
        Action reading = () => UuidV7.GetTimestamp(Guid.NewGuid());

        reading.Should().Throw<ArgumentException>().WithMessage("*version 7*");
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y.AsSpan());
    }
}
