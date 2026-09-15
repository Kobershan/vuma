using VumaRetail.Domain.Imports;

namespace VumaRetail.UnitTests.Imports;

/// <summary>Exercises the import aggregate's safety-critical state transitions.</summary>
public sealed class ImportBatchStateTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Remapping_invalidates_previous_verdicts_and_recounts_rows()
    {
        ImportBatch batch = BatchWithRows(2, 3);
        Map(batch);
        batch.RecordValidation(new Dictionary<int, ImportRowVerdict>
        {
            [2] = ImportRowVerdict.Valid(new Dictionary<string, string?> { ["code"] = "A" }),
            [3] = ImportRowVerdict.Skip(new Dictionary<string, string?> { ["code"] = "B" }),
        });

        batch.ValidRows.Should().Be(1);
        batch.SkippedRows.Should().Be(1);

        Map(batch);

        batch.Status.Should().Be(ImportBatchStatus.Mapped);
        batch.Rows.Should().OnlyContain(row => row.Status == ImportRowStatus.Invalid);
        batch.InvalidRows.Should().Be(2);
        batch.ValidRows.Should().Be(0);
        batch.SkippedRows.Should().Be(0);
    }

    [Fact]
    public void Validation_marks_missing_verdict_invalid_and_preserves_skip_and_valid_counts()
    {
        ImportBatch batch = BatchWithRows(2, 3, 4);
        Map(batch);

        batch.RecordValidation(new Dictionary<int, ImportRowVerdict>
        {
            [2] = ImportRowVerdict.Valid(new Dictionary<string, string?> { ["code"] = "A" }),
            [3] = ImportRowVerdict.Skip(new Dictionary<string, string?> { ["code"] = "B" }),
        });

        batch.Status.Should().Be(ImportBatchStatus.Validated);
        batch.ValidRows.Should().Be(1);
        batch.SkippedRows.Should().Be(1);
        batch.InvalidRows.Should().Be(1);
        batch.FindRow(4)!.Errors.Should().ContainSingle(error => error.Code == "IMPORTS_NOT_VALIDATED");
    }

    [Fact]
    public void Committable_rows_are_sorted_and_compensatable_rows_are_reverse_sorted()
    {
        ImportBatch batch = BatchWithRows(4, 2, 3);
        Map(batch);
        batch.RecordValidation(new Dictionary<int, ImportRowVerdict>
        {
            [2] = ImportRowVerdict.Valid(new Dictionary<string, string?>()),
            [3] = ImportRowVerdict.Valid(new Dictionary<string, string?>()),
            [4] = ImportRowVerdict.Skip(new Dictionary<string, string?>()),
        });

        batch.CommittableRows.Select(row => row.RowNumber).Should().Equal(2, 3);
        batch.BeginCommit();
        batch.RecordRowCommitted(3, ImportRowOutcome.Updated, Guid.NewGuid(), "before");
        batch.RecordRowCommitted(2, ImportRowOutcome.Created, Guid.NewGuid(), null);
        batch.Commit(DateTimeOffset.UtcNow, "operator");

        batch.CompensatableRows.Select(row => row.RowNumber).Should().Equal(3, 2);
        batch.RecordRowRolledBack(3, Guid.NewGuid());
        batch.RecordRowRolledBack(2);
        batch.RollBack(DateTimeOffset.UtcNow, "operator", "wrong file");

        batch.Status.Should().Be(ImportBatchStatus.RolledBack);
        batch.Rows.Should().OnlyContain(row =>
            row.Status == ImportRowStatus.RolledBack || row.Status == ImportRowStatus.Skipped);
        batch.CreatedRows.Should().Be(0);
        batch.UpdatedRows.Should().Be(0);
    }

    [Fact]
    public void Commit_and_discard_refuse_invalid_lifecycle_states()
    {
        ImportBatch batch = BatchWithRows(2);

        Action begin = batch.BeginCommit;
        begin.Should().Throw<ImportRuleException>();
        Action discard = () => batch.Discard();
        discard.Should().NotThrow();
        batch.Status.Should().Be(ImportBatchStatus.Discarded);

        ImportBatch committed = BatchWithRows(2);
        Map(committed);
        committed.RecordValidation(new Dictionary<int, ImportRowVerdict>
        {
            [2] = ImportRowVerdict.Valid(new Dictionary<string, string?>()),
        });
        committed.BeginCommit();
        committed.RecordRowCommitted(2, ImportRowOutcome.Created, Guid.NewGuid(), null);
        committed.Commit(DateTimeOffset.UtcNow, "operator");

        Action discardCommitted = () => committed.Discard();
        discardCommitted.Should().Throw<ImportRuleException>();
    }

    [Fact]
    public void Parse_rejects_empty_rows_and_duplicate_headers()
    {
        ImportBatch empty = NewBatch();
        Action noRows = () => empty.RecordParse(["code"], []);
        noRows.Should().Throw<ImportSourceException>().Which.Code.Should().Be("IMPORTS_SOURCE_HAS_NO_ROWS");

        ImportBatch duplicate = NewBatch();
        Action duplicateHeader = () => duplicate.RecordParse(
            ["Code", "code"],
            [ImportRow.Create(TenantId, null, duplicate.Id, 2, new Dictionary<string, string?>())]);
        duplicateHeader.Should().Throw<ImportSourceException>().Which.Code.Should().Be("IMPORTS_DUPLICATE_HEADER");
    }

    [Fact]
    public void Recording_an_unknown_row_is_a_not_found_error()
    {
        ImportBatch batch = BatchWithRows(2);

        Action record = () => batch.RecordRowSkipped(99);

        record.Should().Throw<ImportNotFoundException>();
    }

    private static ImportBatch BatchWithRows(params int[] rowNumbers)
    {
        ImportBatch batch = NewBatch();

        batch.RecordParse(
            ["code"],
            [.. rowNumbers.Select(number => ImportRow.Create(
                TenantId, null, batch.Id, number, new Dictionary<string, string?> { ["code"] = $"C{number}" }))]);
        return batch;
    }

    private static ImportBatch NewBatch()
        => ImportBatch.Create(
            TenantId, null, "IMP-0001", ImportTargetKind.Suppliers, ImportSourceFormat.Csv,
            "suppliers.csv", "abc123", 10, ImportDuplicateStrategy.Skip);

    private static void Map(ImportBatch batch, string sourceColumn = "code")
        => batch.SetMapping(
            [ImportColumnMapping.Create(TenantId, null, batch.Id, "code", sourceColumn, null)],
            ["code"]);
}
