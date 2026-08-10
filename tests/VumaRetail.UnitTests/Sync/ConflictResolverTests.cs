using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;
using VumaRetail.Sync.Protocol;

namespace VumaRetail.UnitTests.Sync;

/// <summary>
/// The five conflict policies of ADR-007, enumerated rather than sampled.
/// </summary>
/// <remarks>
/// A table because the space is small enough to close and the awkward combinations are the ones that
/// matter: <c>StoreWins</c> evaluated <em>on</em> the store rather than on the cloud,
/// <c>AppendOnly</c> with an older remote stamp, an authority policy where neither node is the
/// authority. Each of those is a plausible wrong answer that a spot-check would miss and that would
/// surface as somebody's edit quietly disappearing.
/// </remarks>
public sealed class ConflictResolverTests
{
    private static readonly HlcStamp Older = new(1_000, 0, "store:a");
    private static readonly HlcStamp Newer = new(2_000, 0, "cloud");

    [Theory]
    // Order decides. An equal stamp is the same write arriving twice, and is left alone.
    [InlineData(ConflictPolicy.LastWriterWins, false, ConflictOutcome.KeepLocal)]
    [InlineData(ConflictPolicy.LastWriterWins, true, ConflictOutcome.TakeRemote)]
    public void LastWriterWins_follows_the_stamps(
        ConflictPolicy policy,
        bool remoteIsNewer,
        ConflictOutcome expected)
    {
        ConflictResolver.Resolve(
            policy,
            remoteIsNewer ? Older : Newer,
            remoteIsNewer ? Newer : Older,
            NodeKind.Store,
            NodeKind.Cloud).Should().Be(expected);
    }

    [Fact]
    public void LastWriterWins_keeps_the_local_row_when_the_stamps_are_equal()
    {
        ConflictResolver.Resolve(
            ConflictPolicy.LastWriterWins,
            Older,
            Older,
            NodeKind.Store,
            NodeKind.Cloud).Should().Be(ConflictOutcome.KeepLocal);
    }

    [Theory]
    // The store is the authority. Evaluated on the store, the local row wins whatever the clocks
    // say — R2: the store is the source of truth for the store, so a later cloud edit still loses.
    [InlineData(NodeKind.Store, NodeKind.Cloud, ConflictOutcome.KeepLocal)]
    // Evaluated on the cloud, the store's incoming version wins.
    [InlineData(NodeKind.Cloud, NodeKind.Store, ConflictOutcome.TakeRemote)]
    // Neither side is the authority the policy names. There is no basis to pick a winner and
    // guessing is how somebody's edit disappears, so it goes to a person.
    [InlineData(NodeKind.Terminal, NodeKind.Cloud, ConflictOutcome.Review)]
    public void StoreWins_follows_authority_and_ignores_the_stamps(
        NodeKind local,
        NodeKind remote,
        ConflictOutcome expected)
    {
        // The remote stamp is deliberately the newer one in every case, so a resolver that had
        // quietly fallen back to last-writer-wins would answer TakeRemote throughout and fail two
        // of the three rows.
        ConflictResolver.Resolve(ConflictPolicy.StoreWins, Older, Newer, local, remote)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(NodeKind.Cloud, NodeKind.Store, ConflictOutcome.KeepLocal)]
    [InlineData(NodeKind.Store, NodeKind.Cloud, ConflictOutcome.TakeRemote)]
    [InlineData(NodeKind.Terminal, NodeKind.Store, ConflictOutcome.Review)]
    public void CloudWins_follows_authority_and_ignores_the_stamps(
        NodeKind local,
        NodeKind remote,
        ConflictOutcome expected)
    {
        ConflictResolver.Resolve(ConflictPolicy.CloudWins, Newer, Older, local, remote)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(NodeKind.Store, NodeKind.Cloud)]
    [InlineData(NodeKind.Cloud, NodeKind.Store)]
    [InlineData(NodeKind.Terminal, NodeKind.Store)]
    public void AppendOnly_never_overwrites_whatever_the_stamps_or_the_tiers_say(
        NodeKind local,
        NodeKind remote)
    {
        // ADR-005. A stock ledger line or a journal from another node is another line, not a
        // competing version — so there is nothing to overwrite and nothing to escalate, in either
        // stamp order.
        ConflictResolver.Resolve(ConflictPolicy.AppendOnly, Older, Newer, local, remote)
            .Should().Be(ConflictOutcome.Append);

        ConflictResolver.Resolve(ConflictPolicy.AppendOnly, Newer, Older, local, remote)
            .Should().Be(ConflictOutcome.Append);
    }

    [Theory]
    [InlineData(NodeKind.Store, NodeKind.Cloud)]
    [InlineData(NodeKind.Cloud, NodeKind.Store)]
    public void ManualReview_always_escalates(NodeKind local, NodeKind remote)
    {
        // Two stores editing one supplier. Picking a winner automatically would silently discard a
        // real decision somebody made, so a newer remote stamp changes nothing.
        ConflictResolver.Resolve(ConflictPolicy.ManualReview, Older, Newer, local, remote)
            .Should().Be(ConflictOutcome.Review);

        ConflictResolver.Resolve(ConflictPolicy.ManualReview, Newer, Older, local, remote)
            .Should().Be(ConflictOutcome.Review);
    }

    [Fact]
    public void An_unrecognised_policy_escalates_rather_than_guessing()
    {
        // Defence against a future enum value reaching an older node. Silently applying or silently
        // discarding are both worse than a queue entry a person looks at.
        ConflictResolver.Resolve((ConflictPolicy)99, Older, Newer, NodeKind.Store, NodeKind.Cloud)
            .Should().Be(ConflictOutcome.Review);
    }

    [Fact]
    public void Every_declared_policy_has_a_defined_answer()
    {
        // The completeness check. A policy added to the enum without a case here would fall to the
        // default and escalate everything on that entity — which nobody would notice until the
        // review queue filled up.
        foreach (ConflictPolicy policy in Enum.GetValues<ConflictPolicy>())
        {
            ConflictOutcome outcome = ConflictResolver.Resolve(
                policy,
                Older,
                Newer,
                NodeKind.Store,
                NodeKind.Cloud);

            Enum.IsDefined(outcome).Should().BeTrue($"{policy} must resolve to a defined outcome");
        }
    }
}
