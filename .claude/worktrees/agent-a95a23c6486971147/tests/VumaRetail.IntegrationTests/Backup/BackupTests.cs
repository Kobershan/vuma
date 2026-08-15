using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using VumaRetail.Application.Abstractions.Backup;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Domain.Backup;
using VumaRetail.Domain.Identity;
using VumaRetail.Infrastructure.Backup;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Sync;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.IntegrationTests.Sync;

namespace VumaRetail.IntegrationTests.Backup;

/// <summary>
/// Requirement R4, end to end, against real PostgreSQL: dump → encrypt → vault → restore → verify.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the drill.</b> R4 is not "backups are taken" — it is "store burns down → new box, run
/// restore, back trading", which is a claim about a <em>restore</em> and can only be supported by
/// having done one. So <see cref="A_snapshot_restores_into_an_empty_database"/> takes a real snapshot
/// of a real database with real <c>pg_dump</c>, encrypts it, puts it in a vault, and restores it into
/// a database that has never had a schema — then counts the rows.
/// </para>
/// <para>
/// The vault is <see cref="FileSystemBackupVault"/>, which is a working implementation rather than a
/// stub: pointed at a NAS it is what a single-store customer with no cloud subscription actually
/// uses. The S3 adapter takes the same path through <see cref="IBackupVault"/> and is untestable here
/// because nothing in this repository has a bucket — recorded in <c>docs/PROGRESS.md</c> under
/// "Deferred — needs real credentials" rather than quietly skipped.
/// </para>
/// <para>
/// Stage 31 owns the full DR exercise. This stage owns the restore path it will exercise, and proves
/// it on every CI run.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class BackupTests(PostgresFixture fixture) : IDisposable
{
    private readonly List<string> _scratchDirectories = [];

    [Fact]
    public async Task A_snapshot_restores_into_an_empty_database()
    {
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        // Something recognisable to look for on the other side. Three roles rather than one, so a
        // restore that produced an empty-but-valid schema fails rather than passing on a row count
        // that happens to match.
        for (int index = 0; index < 3; index++)
        {
            await node.SendAsync(new CreateRoleCommand($"Role {index}", ["identity.user.view"]));
        }

        int rolesBefore = await node.Context.Roles.CountAsync();
        rolesBefore.Should().Be(3);

        IBackupService backups = BuildService(node, out _);

        BackupSnapshot snapshot = await backups.CreateSnapshotAsync();

        snapshot.Status.Should().Be(BackupStatus.Completed);
        snapshot.SizeBytes.Should().BeGreaterThan(0);
        snapshot.Checksum.Should().HaveLength(64);

        // A brand new database with no schema at all — the "new box" in R4.
        string restoredConnection = await fixture.CreateEmptyDatabaseAsync();

        await backups.RestoreSnapshotAsync(snapshot.Id, restoredConnection);

        await using VumaRetailDbContext restored = TestDbContextFactory.For(
            restoredConnection,
            new TestClock(),
            new TestPrincipalAccessor("user:drill"),
            TestTenantContext.Unfiltered());

        (await restored.Roles.CountAsync()).Should().Be(rolesBefore);
        (await restored.Tenants.CountAsync()).Should().Be(1);
        (await restored.Stores.CountAsync()).Should().Be(1);

        List<string> names = await restored.Roles.Select(role => role.Name).Order().ToListAsync();
        names.Should().Equal("Role 0", "Role 1", "Role 2");

        // The ledger records that a restore happened. Without this, R4 is a hope with a cron job.
        snapshot.RestoredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task The_snapshot_in_the_vault_is_not_readable_plaintext()
    {
        // A snapshot is every row a tenant has — customers, staff, prices, margins. It is the
        // vendor's problem the moment it lands in a bucket the vendor operates (docs/SECURITY.md,
        // POPIA), so it is encrypted with a key the storage credential does not unlock.
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        await node.SendAsync(new CreateRoleCommand("VeryDistinctiveRoleName", ["identity.user.view"]));

        IBackupService backups = BuildService(node, out IBackupVault vault);

        BackupSnapshot snapshot = await backups.CreateSnapshotAsync();

        await using Stream stored = await vault.GetAsync(snapshot.ObjectKey);
        using StreamReader reader = new(stored);
        string raw = await reader.ReadToEndAsync();

        raw.Should().NotContain("VeryDistinctiveRoleName");
        raw.Should().StartWith("VUMASNAP", "the framing header is the only plaintext in the object");
    }

    [Fact]
    public async Task Verification_confirms_the_object_is_what_was_written()
    {
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        IBackupService backups = BuildService(node, out _);

        BackupSnapshot snapshot = await backups.CreateSnapshotAsync();

        await backups.VerifySnapshotAsync(snapshot.Id);

        snapshot.VerifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_tampered_snapshot_fails_verification_and_will_not_restore()
    {
        // The failure R4 is actually about. A restore that proceeded on a corrupted object would
        // produce a database that looks restored and fails at the till — worse than not restoring at
        // all, because the shop would stop looking for the real backup.
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        IBackupService backups = BuildService(node, out IBackupVault vault, out string vaultDirectory);

        BackupSnapshot snapshot = await backups.CreateSnapshotAsync();

        string objectPath = Path.Combine(vaultDirectory, snapshot.ObjectKey.Replace('/', Path.DirectorySeparatorChar));
        byte[] bytes = await File.ReadAllBytesAsync(objectPath);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(objectPath, bytes);

        Func<Task> verify = () => backups.VerifySnapshotAsync(snapshot.Id);
        await verify.Should().ThrowAsync<SnapshotChecksumMismatchException>();

        string target = await fixture.CreateEmptyDatabaseAsync();

        Func<Task> restore = () => backups.RestoreSnapshotAsync(snapshot.Id, target);
        (await restore.Should().ThrowAsync<SnapshotChecksumMismatchException>())
            .Which.Code.Should().Be("BACKUP_SNAPSHOT_CHECKSUM_MISMATCH");

        // Nothing was written to the target. The checksum is confirmed before a single byte moves.
        await using NpgsqlConnection connection = new(target);
        await connection.OpenAsync();
        await using NpgsqlCommand tables = connection.CreateCommand();
        tables.CommandText = "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'identity'";
        Convert.ToInt64(await tables.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(0);
    }

    [Fact]
    public async Task A_snapshot_written_with_one_key_will_not_decrypt_with_another()
    {
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        string vaultDirectory = Scratch();
        BackupVaultOptions vaultOptions = new() { Provider = "FileSystem", Directory = vaultDirectory };
        IBackupVault vault = new FileSystemBackupVault(vaultOptions);

        IBackupService writer = BuildService(node, vault, NewKey());
        BackupSnapshot snapshot = await writer.CreateSnapshotAsync();

        IBackupService reader = BuildService(node, vault, NewKey());

        string target = await fixture.CreateEmptyDatabaseAsync();

        Func<Task> restore = () => reader.RestoreSnapshotAsync(snapshot.Id, target);

        await restore.Should().ThrowAsync<SnapshotDecryptionException>();
    }

    [Fact]
    public async Task A_run_that_fails_is_recorded_rather_than_lost()
    {
        // A run that only recorded itself on success would make a crashed backup indistinguishable
        // from a backup nobody scheduled, and those want very different phone calls.
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        BackupSnapshotRepository ledger = new(node.Context);

        IBackupService backups = new BackupService(
            new FailingEngine(),
            new AesGcmSnapshotCipher(new SnapshotEncryptionOptions { Key = NewKey() }),
            new FileSystemBackupVault(new BackupVaultOptions { Provider = "FileSystem", Directory = Scratch() }),
            ledger,
            node.Context,
            node.TenantContext,
            new NodeIdentity(node.Node),
            node.Clock,
            NullLogger<BackupService>.Instance);

        Func<Task> run = () => backups.CreateSnapshotAsync();

        await run.Should().ThrowAsync<BackupEngineException>();

        node.Context.ChangeTracker.Clear();

        BackupSnapshot recorded = await node.Context.BackupSnapshots.SingleAsync();
        recorded.Status.Should().Be(BackupStatus.Failed);
        recorded.Error.Should().Contain("pg_dump");
        recorded.IsRestorable.Should().BeFalse();
    }

    [Fact]
    public async Task The_object_key_is_tenant_prefixed()
    {
        // One bucket holds many tenants, so a storage-layer prefix policy can separate them as well
        // as the code does. A key that was not tenant-prefixed would make that impossible to add
        // later without rewriting every ledger row.
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        IBackupService backups = BuildService(node, out _);

        BackupSnapshot snapshot = await backups.CreateSnapshotAsync();

        snapshot.ObjectKey.Should().StartWith($"tenants/{node.TenantId:D}/");
        snapshot.ObjectKey.Should().EndWith(".vsnap");
    }

    private IBackupService BuildService(SyncHarness node, out IBackupVault vault)
        => BuildService(node, out vault, out _);

    private IBackupService BuildService(SyncHarness node, out IBackupVault vault, out string vaultDirectory)
    {
        vaultDirectory = Scratch();
        vault = new FileSystemBackupVault(new BackupVaultOptions
        {
            Provider = "FileSystem",
            Directory = vaultDirectory,
        });

        return BuildService(node, vault, NewKey());
    }

    private static IBackupService BuildService(SyncHarness node, IBackupVault vault, string key)
        => new BackupService(
            new PostgresBackupEngine(
                node.ConnectionString,
                new PostgresBackupOptions(),
                NullLogger<PostgresBackupEngine>.Instance),
            new AesGcmSnapshotCipher(new SnapshotEncryptionOptions { Key = key }),
            vault,
            new BackupSnapshotRepository(node.Context),
            node.Context,
            node.TenantContext,
            new NodeIdentity(node.Node),
            node.Clock,
            NullLogger<BackupService>.Instance);

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private string Scratch()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"vuma-vault-{Guid.NewGuid():N}");
        _scratchDirectories.Add(directory);

        return directory;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (string directory in _scratchDirectories.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An engine that always fails, standing in for a missing or broken <c>pg_dump</c>.</summary>
    private sealed class FailingEngine : IBackupEngine
    {
        public Task<long> DumpAsync(Stream destination, CancellationToken cancellationToken = default)
            => throw new BackupEngineException("pg_dump exited with code 1.");

        public Task RestoreAsync(
            Stream source,
            string targetConnectionString,
            CancellationToken cancellationToken = default)
            => throw new BackupEngineException("pg_restore exited with code 1.");
    }
}

/// <summary>The snapshot cipher on its own, at the boundaries that make it worth having.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SnapshotCipherTests
{
    [Fact]
    public async Task Round_trips_a_payload_larger_than_one_chunk()
    {
        // GCM is a one-shot construction, so a snapshot has to be framed into chunks. Anything at or
        // below the chunk size exercises one frame and proves nothing about the framing.
        byte[] plaintext = RandomNumberGenerator.GetBytes((3 * 1024 * 1024) + 7);

        AesGcmSnapshotCipher cipher = new(new SnapshotEncryptionOptions { Key = NewKey() });

        using MemoryStream encrypted = new();
        await cipher.EncryptAsync(new MemoryStream(plaintext), encrypted);

        encrypted.Position = 0;
        using MemoryStream decrypted = new();
        await cipher.DecryptAsync(encrypted, decrypted);

        decrypted.ToArray().Should().Equal(plaintext);
    }

    [Fact]
    public async Task Round_trips_an_empty_payload()
    {
        AesGcmSnapshotCipher cipher = new(new SnapshotEncryptionOptions { Key = NewKey() });

        using MemoryStream encrypted = new();
        await cipher.EncryptAsync(new MemoryStream([]), encrypted);

        encrypted.Position = 0;
        using MemoryStream decrypted = new();
        await cipher.DecryptAsync(encrypted, decrypted);

        decrypted.Length.Should().Be(0);
    }

    [Fact]
    public async Task Refuses_a_truncated_snapshot()
    {
        // Truncation is the failure mode a chunked format is most likely to accept quietly: every
        // frame that arrived is individually valid. The authenticated terminator is what catches it.
        AesGcmSnapshotCipher cipher = new(new SnapshotEncryptionOptions { Key = NewKey() });

        using MemoryStream encrypted = new();
        await cipher.EncryptAsync(new MemoryStream(RandomNumberGenerator.GetBytes(2 * 1024 * 1024)), encrypted);

        byte[] truncated = encrypted.ToArray()[..(encrypted.ToArray().Length / 2)];

        Func<Task> decrypt = () => cipher.DecryptAsync(new MemoryStream(truncated), new MemoryStream());

        await decrypt.Should().ThrowAsync<SnapshotDecryptionException>();
    }

    [Fact]
    public async Task Refuses_a_snapshot_that_is_not_one()
    {
        AesGcmSnapshotCipher cipher = new(new SnapshotEncryptionOptions { Key = NewKey() });

        Func<Task> decrypt = () => cipher.DecryptAsync(
            new MemoryStream("this is not a snapshot"u8.ToArray()),
            new MemoryStream());

        await decrypt.Should().ThrowAsync<SnapshotDecryptionException>();
    }

    [Fact]
    public async Task Refuses_to_write_a_snapshot_with_no_key_configured()
    {
        AesGcmSnapshotCipher cipher = new(new SnapshotEncryptionOptions());

        Func<Task> encrypt = () => cipher.EncryptAsync(new MemoryStream([1, 2, 3]), new MemoryStream());

        await encrypt.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not configured*");
    }

    [Theory]
    [InlineData("not base64 at all!")]
    [InlineData("c2hvcnQ=")]
    public async Task Refuses_a_key_that_is_not_256_bits_of_base64(string key)
    {
        AesGcmSnapshotCipher cipher = new(new SnapshotEncryptionOptions { Key = key });

        Func<Task> encrypt = () => cipher.EncryptAsync(new MemoryStream([1, 2, 3]), new MemoryStream());

        await encrypt.Should().ThrowAsync<InvalidOperationException>();
    }

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
