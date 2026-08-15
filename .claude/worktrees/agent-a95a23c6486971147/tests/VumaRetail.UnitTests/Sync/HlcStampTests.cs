using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Sync;

/// <summary>
/// The ordering device every replicated change carries (ADR-007).
/// </summary>
/// <remarks>
/// The property that has to hold is that <b>the string form sorts the same way the value compares</b>.
/// The outbox is drained with <c>ORDER BY operation_stamp</c> and the cursor is one indexed column,
/// so the day the zero-padding is dropped nothing throws — sync simply starts shipping in the wrong
/// order and skipping rows, months later, on the store with the most traffic.
/// </remarks>
public sealed class HlcStampTests
{
    [Fact]
    public void Orders_by_physical_time_first()
    {
        HlcStamp earlier = new(1_000, 99, "store:z");
        HlcStamp later = new(1_001, 0, "store:a");

        (earlier < later).Should().BeTrue();
    }

    [Fact]
    public void Orders_by_counter_when_the_physical_time_matches()
    {
        HlcStamp earlier = new(1_000, 1, "store:z");
        HlcStamp later = new(1_000, 2, "store:a");

        (earlier < later).Should().BeTrue();
    }

    [Fact]
    public void Orders_by_node_id_when_physical_time_and_counter_both_match()
    {
        // The tie-break that makes the order *total* rather than partial. Two nodes that
        // independently stamp the same millisecond and counter must still agree on which came
        // first, without talking — otherwise last-writer-wins resolves differently at each end and
        // the two converge on two different answers.
        HlcStamp first = new(1_000, 5, "store:a");
        HlcStamp second = new(1_000, 5, "store:b");

        (first < second).Should().BeTrue();
        (second > first).Should().BeTrue();
    }

    [Fact]
    public void Equal_stamps_compare_equal()
    {
        HlcStamp left = new(1_000, 5, "store:a");
        HlcStamp right = new(1_000, 5, "store:a");

        left.CompareTo(right).Should().Be(0);
        (left <= right).Should().BeTrue();
        (left >= right).Should().BeTrue();
    }

    [Fact]
    public void Round_trips_through_its_string_form()
    {
        HlcStamp original = new(1_754_000_000_123, 42, "store:jhb01");

        HlcStamp.Parse(original.ToString()).Should().Be(original);
    }

    [Fact]
    public void Sorts_lexically_in_the_same_order_it_compares()
    {
        // The load-bearing assertion. Every one of these pairs crosses a digit-count boundary, which
        // is exactly where an unpadded encoding would sort "9" after "10".
        HlcStamp[] ascending =
        [
            new(9, 0, "a"),
            new(10, 0, "a"),
            new(99, 9, "a"),
            new(100, 0, "a"),
            new(1_000, 9, "a"),
            new(1_000, 10, "a"),
            new(1_000, 100, "a"),
            new(1_754_000_000_000, 0, "a"),
            new(1_754_000_000_001, 0, "a"),
        ];

        string[] byString = [.. ascending.Select(stamp => stamp.ToString()).Order(StringComparer.Ordinal)];
        string[] byValue = [.. ascending.Order().Select(stamp => stamp.ToString())];

        byString.Should().Equal(byValue);
        byValue.Should().Equal([.. ascending.Select(stamp => stamp.ToString())]);
    }

    [Fact]
    public void MinValue_precedes_every_issued_stamp()
    {
        HlcStamp.MinValue.IsEmpty.Should().BeTrue();
        (HlcStamp.MinValue < new HlcStamp(1, 0, "a")).Should().BeTrue();
    }

    [Fact]
    public void Parses_an_empty_string_as_MinValue()
    {
        // The default on a row never captured for replication. It has to parse rather than throw, or
        // every conflict comparison against a pre-Stage-04 row would fail instead of losing.
        HlcStamp.Parse(string.Empty).Should().Be(HlcStamp.MinValue);
    }

    [Theory]
    [InlineData("not a stamp")]
    [InlineData("1000:5")]
    [InlineData("abc:5:node")]
    [InlineData("1000:xyz:node")]
    [InlineData("-1:5:node")]
    public void Refuses_a_string_that_is_not_a_stamp(string value)
    {
        Action parse = () => HlcStamp.Parse(value);

        parse.Should().Throw<MalformedHlcStampException>()
            .Which.Code.Should().Be("SYNC_STAMP_MALFORMED");
    }

    [Fact]
    public void Refuses_a_node_id_longer_than_the_column()
    {
        string oversized = new('n', HlcStamp.NodeIdMaxLength + 1);

        Action parse = () => HlcStamp.Parse($"00000000001000:000005:{oversized}");

        parse.Should().Throw<MalformedHlcStampException>();
    }
}
