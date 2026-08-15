using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VumaRetail.Contracts;
using VumaRetail.Contracts.Backup;
using VumaRetail.Contracts.Sync;
using VumaRetail.Domain.Primitives;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Sync.Permissions;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>
/// The Stage 04 endpoints over HTTP, against the store server running its own <c>Program.cs</c>
/// (<c>docs/TESTING.md</c> §1).
/// </summary>
/// <remarks>
/// <c>POST /sync/batches</c> is the highest-privilege endpoint in the system — it writes replicated
/// rows without passing a single business rule — so the tests that matter here are the ones about who
/// may call it and what it does with a malformed batch, not the happy path.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SyncApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Pushing_a_batch_without_the_permission_is_forbidden()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("cashier");

        using HttpClient signedIn = await harness.SignInAsync("cashier");

        HttpResponseMessage response = await signedIn.PostAsJsonAsync("/api/v1/sync/batches", EmptyBatch(harness));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be(ApiErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Pushing_a_batch_unauthenticated_is_unauthorised()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            "/api/v1/sync/batches",
            EmptyBatch(harness));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_empty_batch_is_accepted_as_a_heartbeat()
    {
        // "We have heard from this store" is worth knowing on its own — it is what tells an operator
        // the link is up and the queue is genuinely empty rather than stuck.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("peer", permissions: SyncPermissions.BatchPush);

        using HttpClient peer = await harness.SignInAsync("peer");

        HttpResponseMessage response = await peer.PostAsJsonAsync("/api/v1/sync/batches", EmptyBatch(harness));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        SyncBatchResponse? acknowledgement = await response.Content.ReadFromJsonAsync<SyncBatchResponse>();
        acknowledgement.Should().NotBeNull();
        acknowledgement!.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task A_batch_naming_an_unknown_source_kind_is_a_bad_request()
    {
        // Never a silent fallback. A SourceKind that defaulted to Store would let a terminal win a
        // StoreWins conflict against the store server it is plugged into.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("peer", permissions: SyncPermissions.BatchPush);

        using HttpClient peer = await harness.SignInAsync("peer");

        HttpResponseMessage response = await peer.PostAsJsonAsync(
            "/api/v1/sync/batches",
            EmptyBatch(harness) with { SourceKind = "Satellite" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("SYNC_FIELD_MALFORMED");
    }

    [Fact]
    public async Task A_batch_with_a_malformed_stamp_is_a_bad_request()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("peer", permissions: SyncPermissions.BatchPush);

        using HttpClient peer = await harness.SignInAsync("peer");

        HttpResponseMessage response = await peer.PostAsJsonAsync(
            "/api/v1/sync/batches",
            EmptyBatch(harness) with
            {
                Operations =
                [
                    new SyncOperationDto(
                        UuidV7.NewGuid(),
                        "Role",
                        UuidV7.NewGuid(),
                        "Upsert",
                        "not a stamp",
                        "{}",
                        DateTimeOffset.UtcNow),
                ],
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("SYNC_STAMP_MALFORMED");
    }

    [Fact]
    public async Task A_batch_beyond_the_size_ceiling_is_refused_by_validation()
    {
        // The ceiling is what stops a peer holding a write transaction open on a trading store for as
        // long as it takes to apply a hundred thousand operations.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("peer", permissions: SyncPermissions.BatchPush);

        using HttpClient peer = await harness.SignInAsync("peer");

        SyncOperationDto[] tooMany =
        [
            .. Enumerable.Range(0, VumaRetail.Sync.Commands.ReceiveSyncBatchCommandValidator.MaxOperationsPerBatch + 1)
                .Select(_ => new SyncOperationDto(
                    UuidV7.NewGuid(),
                    "Role",
                    UuidV7.NewGuid(),
                    "Upsert",
                    new HlcStamp(1_000, 0, "cloud").ToString(),
                    "{}",
                    DateTimeOffset.UtcNow)),
        ];

        HttpResponseMessage response = await peer.PostAsJsonAsync(
            "/api/v1/sync/batches",
            EmptyBatch(harness) with { Operations = tooMany });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).GetProperty("code").GetString()
            .Should().Be(ApiErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Sync_status_answers_with_this_nodes_health()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("manager", permissions: SyncPermissions.StatusView);

        using HttpClient signedIn = await harness.SignInAsync("manager");

        SyncStatusResponse? status = await signedIn.GetFromJsonAsync<SyncStatusResponse>("/api/v1/sync/status");

        status.Should().NotBeNull();
        status!.NodeId.Should().NotBeEmpty();
        status.NodeKind.Should().Be("Store");
        status.OpenConflicts.Should().Be(0);
        status.Cursors.Should().BeEmpty("nothing has been shipped or received yet");
    }

    [Fact]
    public async Task The_conflict_queue_answers_and_starts_empty()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("manager", permissions: SyncPermissions.ConflictView);

        using HttpClient signedIn = await harness.SignInAsync("manager");

        IReadOnlyList<ConflictResponse>? conflicts = await signedIn
            .GetFromJsonAsync<IReadOnlyList<ConflictResponse>>("/api/v1/sync/conflicts");

        conflicts.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task Resolving_a_conflict_that_does_not_exist_is_a_not_found_problem()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("manager", permissions: SyncPermissions.ConflictResolve);

        using HttpClient signedIn = await harness.SignInAsync("manager");

        HttpResponseMessage response = await signedIn.PostAsJsonAsync(
            $"/api/v1/sync/conflicts/{Guid.NewGuid()}/resolve",
            new ResolveConflictRequest("KeptLocal", "our version is right"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblemAsync(response)).GetProperty("code").GetString()
            .Should().Be("SYNC_CONFLICT_NOT_FOUND");
    }

    [Fact]
    public async Task Resolving_a_conflict_without_a_reason_is_refused()
    {
        // The note is mandatory because this is the one place a person overrules the system about
        // which version of a business record is real.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("manager", permissions: SyncPermissions.ConflictResolve);

        using HttpClient signedIn = await harness.SignInAsync("manager");

        HttpResponseMessage response = await signedIn.PostAsJsonAsync(
            $"/api/v1/sync/conflicts/{Guid.NewGuid()}/resolve",
            new ResolveConflictRequest("KeptLocal", ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_snapshot_ledger_answers_and_starts_empty()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("manager", permissions: BackupPermissions.SnapshotView);

        using HttpClient signedIn = await harness.SignInAsync("manager");

        IReadOnlyList<BackupSnapshotResponse>? ledger = await signedIn
            .GetFromJsonAsync<IReadOnlyList<BackupSnapshotResponse>>("/api/v1/backup/snapshots");

        ledger.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task Taking_a_backup_without_the_permission_is_forbidden()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("cashier");

        using HttpClient signedIn = await harness.SignInAsync("cashier");

        HttpResponseMessage response = await signedIn.PostAsJsonAsync(
            "/api/v1/backup/snapshots",
            new CreateBackupSnapshotRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_incremental_snapshot_is_refused_because_nothing_implements_it()
    {
        // The enum value exists so adding WAL shipping in Stage 31 is not a schema change. Accepting
        // it today would produce a snapshot that claims to be something it is not.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("manager", permissions: BackupPermissions.SnapshotCreate);

        using HttpClient signedIn = await harness.SignInAsync("manager");

        HttpResponseMessage response = await signedIn.PostAsJsonAsync(
            "/api/v1/backup/snapshots",
            new CreateBackupSnapshotRequest("Incremental"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).GetProperty("code").GetString()
            .Should().Be(ApiErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Verifying_a_snapshot_that_does_not_exist_is_a_not_found_problem()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("manager", permissions: BackupPermissions.SnapshotVerify);

        using HttpClient signedIn = await harness.SignInAsync("manager");

        HttpResponseMessage response = await signedIn.PostAsJsonAsync(
            $"/api/v1/backup/snapshots/{Guid.NewGuid()}/verify",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblemAsync(response)).GetProperty("code").GetString()
            .Should().Be("BACKUP_SNAPSHOT_NOT_FOUND");
    }

    [Fact]
    public async Task Every_new_endpoint_appears_in_the_openapi_document()
    {
        // CLAUDE.md §8: no feature exists that is not in /openapi/v1.json.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        JsonDocument document = JsonDocument.Parse(
            await harness.Client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)));

        JsonElement paths = document.RootElement.GetProperty("paths");

        foreach (string route in (string[])
        [
            "/api/v1/sync/batches",
            "/api/v1/sync/status",
            "/api/v1/sync/conflicts",
            "/api/v1/backup/snapshots",
        ])
        {
            paths.TryGetProperty(route, out _).Should().BeTrue($"{route} must be documented");
        }
    }

    private static SyncBatchRequest EmptyBatch(ApiHarness harness)
        => new("cloud", "Cloud", harness.TenantId, harness.StoreId, []);

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
