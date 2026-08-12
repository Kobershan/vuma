using VumaRetail.Domain.Backup;
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;

namespace VumaRetail.UnitTests.Sync;

/// <summary>The behaviour on the sync entities, which is small and load-bearing.</summary>
public sealed class SyncCursorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Starts_before_every_issued_stamp()
    {
        SyncCursor cursor = SyncCursor.Start(Guid.NewGuid(), null, "cloud", SyncDirection.Outbound);

        cursor.Acknowledged.Should().Be(HlcStamp.MinValue);
        cursor.AcknowledgedCount.Should().Be(0);
        cursor.LastContactAt.Should().BeNull();
    }

    [Fact]
    public void Advances_to_a_newer_stamp()
    {
        SyncCursor cursor = SyncCursor.Start(Guid.NewGuid(), null, "cloud", SyncDirection.Outbound);
        HlcStamp stamp = new(2_000, 0, "store:a");

        cursor.Advance(stamp, operations: 3, Now);

        cursor.Acknowledged.Should().Be(stamp);
        cursor.AcknowledgedCount.Should().Be(3);
        cursor.LastContactAt.Should().Be(Now);
    }

    [Fact]
    public void Does_not_go_backwards_when_a_retried_batch_arrives_out_of_order()
    {
        // Batches can arrive out of order after a retry. A cursor that went backwards would re-send
        // everything between — correct, because the receiver deduplicates, but a store re-uploading
        // a month of sales every time one batch is retried is not a system anyone keeps running.
        SyncCursor cursor = SyncCursor.Start(Guid.NewGuid(), null, "cloud", SyncDirection.Outbound);

        cursor.Advance(new HlcStamp(2_000, 0, "store:a"), operations: 3, Now);
        cursor.Advance(new HlcStamp(1_000, 0, "store:a"), operations: 2, Now.AddSeconds(1));

        cursor.Acknowledged.Should().Be(new HlcStamp(2_000, 0, "store:a"));
        cursor.AcknowledgedCount.Should().Be(3, "the stale batch settled nothing new");
    }

    [Fact]
    public void Records_contact_even_when_nothing_settled()
    {
        // An empty batch is a heartbeat, and "we have heard from this store" is worth knowing on its
        // own — it is what tells an operator the link is up and the queue is genuinely empty.
        SyncCursor cursor = SyncCursor.Start(Guid.NewGuid(), null, "cloud", SyncDirection.Outbound);

        cursor.Advance(HlcStamp.MinValue, operations: 0, Now);

        cursor.LastContactAt.Should().Be(Now);
        cursor.AcknowledgedCount.Should().Be(0);
    }
}

/// <summary>The delivery-state transitions on an outbox row.</summary>
public sealed class OutboxMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Starts_pending_with_no_attempts()
    {
        OutboxMessage message = Capture();

        message.Status.Should().Be(OutboxStatus.Pending);
        message.AttemptCount.Should().Be(0);
        message.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public void Counts_an_attempt_when_it_goes_in_flight()
    {
        OutboxMessage message = Capture();

        message.MarkInFlight();

        message.Status.Should().Be(OutboxStatus.InFlight);
        message.AttemptCount.Should().Be(1);
    }

    [Fact]
    public void Clears_the_error_and_the_backoff_when_it_is_finally_acknowledged()
    {
        OutboxMessage message = Capture();

        message.MarkInFlight();
        message.MarkFailed("the cloud was unreachable", Now.AddSeconds(5));
        message.MarkInFlight();
        message.MarkDispatched(Now.AddMinutes(1));

        message.Status.Should().Be(OutboxStatus.Dispatched);
        message.DispatchedAt.Should().Be(Now.AddMinutes(1));
        message.LastError.Should().BeNull();
        message.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public void Truncates_a_long_error_to_the_column()
    {
        OutboxMessage message = Capture();

        message.MarkFailed(new string('x', OutboxMessage.MaxErrorLength + 500), Now);

        message.LastError.Should().HaveLength(OutboxMessage.MaxErrorLength);
    }

    [Fact]
    public void Round_trips_its_stamp()
    {
        HlcStamp stamp = new(1_754_000_000_123, 7, "store:jhb01");

        Capture(stamp).OperationHlc.Should().Be(stamp);
    }

    private static OutboxMessage Capture(HlcStamp? stamp = null)
        => OutboxMessage.Capture(
            Guid.NewGuid(),
            null,
            UuidV7.NewGuid(),
            "store:a",
            "Store",
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            ReplicationScope.StoreToCloud,
            ConflictPolicy.LastWriterWins,
            stamp ?? new HlcStamp(1_000, 0, "store:a"),
            "{}",
            Now);
}

/// <summary>The review queue's one rule: a decision is recorded once, with a reason.</summary>
public sealed class ConflictEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Opens_unresolved_and_keeps_both_versions()
    {
        ConflictEntry entry = Open();

        entry.IsOpen.Should().BeTrue();
        entry.Resolution.Should().Be(ConflictResolution.Unresolved);
        entry.LocalVersion.Should().Be("""{"Name":"local"}""");
        entry.RemoteVersion.Should().Be("""{"Name":"remote"}""");
    }

    [Fact]
    public void Records_who_decided_and_why()
    {
        ConflictEntry entry = Open();

        entry.Resolve(ConflictResolution.TookRemote, "user:1", "Head office confirmed the new terms", Now);

        entry.Resolution.Should().Be(ConflictResolution.TookRemote);
        entry.ResolvedBy.Should().Be("user:1");
        entry.ResolutionNote.Should().Be("Head office confirmed the new terms");
        entry.ResolvedAt.Should().Be(Now);
        entry.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Refuses_a_second_decision()
    {
        // Re-resolving would overwrite the decision *and* the reason recorded for it — which is the
        // only record of why a person overruled the system about a business record.
        ConflictEntry entry = Open();
        entry.Resolve(ConflictResolution.KeptLocal, "user:1", "our version is right", Now);

        Action again = () => entry.Resolve(ConflictResolution.TookRemote, "user:2", "no it is not", Now);

        again.Should().Throw<ConflictAlreadyResolvedException>()
            .Which.Code.Should().Be("SYNC_CONFLICT_ALREADY_RESOLVED");
    }

    [Fact]
    public void Refuses_a_decision_that_decides_nothing()
    {
        Action unresolved = () => Open().Resolve(ConflictResolution.Unresolved, "user:1", "hmm", Now);

        unresolved.Should().Throw<ConflictNotSettledException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Requires_a_reason(string note)
    {
        Action noReason = () => Open().Resolve(ConflictResolution.KeptLocal, "user:1", note, Now);

        noReason.Should().Throw<ArgumentException>();
    }

    private static ConflictEntry Open()
        => ConflictEntry.Open(
            Guid.NewGuid(),
            null,
            "Store",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "cloud",
            """{"Name":"local"}""",
            """{"Name":"remote"}""",
            new HlcStamp(1_000, 0, "store:a"),
            new HlcStamp(2_000, 0, "cloud"),
            Now);
}

/// <summary>The snapshot ledger — requirement R4's evidence, and what it refuses to claim.</summary>
public sealed class BackupSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Begins_running_so_a_crashed_backup_is_visible()
    {
        // A run that only recorded itself on success would make a crashed backup indistinguishable
        // from a backup nobody scheduled, and those want very different phone calls.
        BackupSnapshot snapshot = Begin();

        snapshot.Status.Should().Be(BackupStatus.Running);
        snapshot.IsRestorable.Should().BeFalse();
        snapshot.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Completes_with_its_size_and_checksum()
    {
        BackupSnapshot snapshot = Begin();

        snapshot.Complete(4_096, "abc123", Now.AddMinutes(2));

        snapshot.Status.Should().Be(BackupStatus.Completed);
        snapshot.IsRestorable.Should().BeTrue();
        snapshot.SizeBytes.Should().Be(4_096);
        snapshot.Checksum.Should().Be("abc123");
    }

    [Fact]
    public void Refuses_to_be_verified_or_restored_before_it_completed()
    {
        BackupSnapshot snapshot = Begin();

        Action verify = () => snapshot.MarkVerified(Now);
        Action restore = () => snapshot.MarkRestored(Now);

        verify.Should().Throw<SnapshotNotRestorableException>();
        restore.Should().Throw<SnapshotNotRestorableException>();
    }

    [Fact]
    public void Refuses_to_be_restored_after_it_failed()
    {
        BackupSnapshot snapshot = Begin();
        snapshot.Fail("the vault was unreachable", Now);

        Action restore = () => snapshot.MarkRestored(Now);

        restore.Should().Throw<SnapshotNotRestorableException>()
            .Which.Code.Should().Be("BACKUP_SNAPSHOT_NOT_RESTORABLE");
    }

    [Fact]
    public void Records_when_it_was_last_verified_and_last_restored()
    {
        // The two fields that make R4 a claim about a restore rather than about a write.
        BackupSnapshot snapshot = Begin();
        snapshot.Complete(4_096, "abc123", Now.AddMinutes(2));

        snapshot.MarkVerified(Now.AddHours(1));
        snapshot.MarkRestored(Now.AddDays(1));

        snapshot.VerifiedAt.Should().Be(Now.AddHours(1));
        snapshot.RestoredAt.Should().Be(Now.AddDays(1));
    }

    private static BackupSnapshot Begin()
        => BackupSnapshot.Begin(Guid.NewGuid(), null, BackupKind.Full, "store:a", "tenants/x/snapshot", Now);
}
