using FluentAssertions;
using VumaRetail.Contracts.Sync;
using VumaRetail.Desktop.Offline;
using Xunit;

namespace VumaRetail.Desktop.Tests;

public sealed class TerminalSyncOutboxTests
{
    [Fact]
    public async Task Operations_survive_restart_and_are_deduplicated_by_operation_id()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vuma-terminal-{Guid.NewGuid():N}.db");
        try
        {
            Guid saleId = Guid.NewGuid();
            Guid operationId = Guid.NewGuid();
            SyncOperationDto operation = Operation(operationId, "Sale", saleId, "Upsert");
            await using (var first = new TerminalSyncOutbox(path))
            {
                await first.EnqueueAsync(saleId, 0, operation);
                await first.EnqueueAsync(saleId, 0, operation);
                (await first.PendingAsync()).Should().ContainSingle();
            }

            await using var reopened = new TerminalSyncOutbox(path);
            (await reopened.PendingAsync()).Should().ContainSingle().Which.OperationId.Should().Be(operationId);
            (await reopened.CountOutstandingAsync()).Should().Be(1);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Applied_and_duplicate_acknowledgements_remove_rows_atomically()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vuma-terminal-{Guid.NewGuid():N}.db");
        try
        {
            Guid saleId = Guid.NewGuid();
            Guid applied = Guid.NewGuid();
            Guid duplicate = Guid.NewGuid();
            await using var outbox = new TerminalSyncOutbox(path);
            await outbox.EnqueueAsync(saleId, 0, Operation(applied, "Sale", saleId, "Upsert"));
            await outbox.EnqueueAsync(saleId, 1, Operation(duplicate, "SaleLine", Guid.NewGuid(), "Upsert"));

            await outbox.SettleAsync(new SyncBatchResponse(
                "store:one", "00000000000000000000000000000001", [
                    new SyncOperationOutcomeDto(applied, "Applied"),
                    new SyncOperationOutcomeDto(duplicate, "Duplicate")
                ]));

            (await outbox.PendingAsync()).Should().BeEmpty();
            (await outbox.CountOutstandingAsync()).Should().Be(0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Rejected_operations_remain_visible_for_review_and_are_not_reported_as_applied()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vuma-terminal-{Guid.NewGuid():N}.db");
        try
        {
            Guid operationId = Guid.NewGuid();
            await using var outbox = new TerminalSyncOutbox(path);
            await outbox.EnqueueAsync(Guid.NewGuid(), 0, Operation(operationId, "Sale", Guid.NewGuid(), "Upsert"));

            await outbox.SettleAsync(new SyncBatchResponse(
                "store:one", "00000000000000000000000000000001", [
                    new SyncOperationOutcomeDto(operationId, "Rejected", Detail: "invalid tenant")
                ]));

            (await outbox.PendingAsync()).Should().BeEmpty();
            (await outbox.CountOutstandingAsync()).Should().Be(1);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static SyncOperationDto Operation(Guid operationId, string entityType, Guid entityId, string operation)
        => new(operationId, entityType, entityId, operation,
            "00000000000000000000000000000001", $"{{\"Id\":\"{entityId:D}\"}}", DateTimeOffset.UtcNow);
}
