using VumaRetail.Domain.Workflow;

namespace VumaRetail.UnitTests.Workflow;

/// <summary>
/// Document version numbering, and that <see cref="Document.CurrentVersionNumber"/> always matches the
/// latest version raised (<c>docs/stages/STAGE-05-workflow.md</c>).
/// </summary>
public sealed class DocumentTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void A_new_document_starts_at_version_one()
    {
        Document document = CreateDocument();

        document.CurrentVersionNumber.Should().Be(1);
        document.IsGenerated.Should().BeFalse();
    }

    [Fact]
    public void A_generated_document_is_flagged_generated_from_creation()
    {
        Document document = Document.Create(
            Tenant, null, "procurement", "purchase-order", Guid.NewGuid(), "generated-report", "PO PDF",
            "application/pdf", DocumentSource.Generated);

        document.IsGenerated.Should().BeTrue();
    }

    [Fact]
    public void Each_new_version_advances_the_current_version_number_by_one()
    {
        Document document = CreateDocument();

        int second = document.RegisterNewVersion("application/pdf", DocumentSource.Uploaded);
        int third = document.RegisterNewVersion("application/pdf", DocumentSource.Generated);

        second.Should().Be(2);
        third.Should().Be(3);
        document.CurrentVersionNumber.Should().Be(3);
    }

    [Fact]
    public void A_new_version_updates_the_content_type_and_source_flag()
    {
        Document document = CreateDocument();

        document.RegisterNewVersion("application/pdf", DocumentSource.Generated);

        document.ContentType.Should().Be("application/pdf");
        document.IsGenerated.Should().BeTrue();
    }

    [Fact]
    public void A_title_longer_than_the_limit_is_truncated_not_refused()
    {
        string longTitle = new('t', Document.MaxTitleLength + 20);

        Document document = Document.Create(
            Tenant, null, "procurement", "purchase-order", Guid.NewGuid(), "attachment", longTitle,
            "application/pdf", DocumentSource.Uploaded);

        document.Title.Should().HaveLength(Document.MaxTitleLength);
    }

    [Fact]
    public void DocumentVersion_records_the_content_hash_size_and_producer()
    {
        DocumentVersion version = DocumentVersion.Record(
            Tenant, null, Guid.NewGuid(), 1, "tenant/doc/0001-hash", new string('a', 64), 2048,
            "application/pdf", DocumentSource.Uploaded, "user:uploader");

        version.VersionNumber.Should().Be(1);
        version.StorageKey.Should().Be("tenant/doc/0001-hash");
        version.ContentHash.Should().HaveLength(DocumentVersion.ContentHashLength);
        version.SizeBytes.Should().Be(2048);
        version.Source.Should().Be(DocumentSource.Uploaded);
        version.GeneratedBy.Should().Be("user:uploader");
    }

    [Fact]
    public void A_negative_size_is_refused()
    {
        Action recording = () => DocumentVersion.Record(
            Tenant, null, Guid.NewGuid(), 1, "key", new string('a', 64), -1,
            "application/pdf", DocumentSource.Uploaded, "user:uploader");

        recording.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static Document CreateDocument() => Document.Create(
        Tenant,
        null,
        "procurement",
        "purchase-order",
        Guid.NewGuid(),
        "attachment",
        "Signed order",
        "application/pdf",
        DocumentSource.Uploaded);
}
