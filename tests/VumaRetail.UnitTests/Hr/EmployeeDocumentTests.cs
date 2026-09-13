using VumaRetail.Domain.HrManagement;
using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Hr;

namespace VumaRetail.UnitTests.Hr;

public sealed class EmployeeDocumentTests
{
    [Fact]
    public void Document_metadata_requires_a_valid_sha256_and_normalises_it()
    {
        string checksum = new string('A', 64);
        var document = EmployeeDocument.Record(Guid.NewGuid(), Guid.NewGuid(), " ID copy ", " hr/employee/id.pdf ", checksum);

        document.DocumentType.Should().Be("ID copy");
        document.BlobKey.Should().Be("hr/employee/id.pdf");
        document.ContentSha256.Should().Be(checksum.ToLowerInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-checksum")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!")]
    public void Invalid_checksum_is_rejected(string checksum)
    {
        var action = () => EmployeeDocument.Record(Guid.NewGuid(), Guid.NewGuid(), "Contract", "hr/contract.pdf", checksum);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Document_listing_returns_metadata_without_external_blob_key()
    {
        var document = EmployeeDocument.Record(Guid.NewGuid(), Guid.NewGuid(), "Contract", "private/key.pdf", new string('b', 64));
        var repository = Substitute.For<IEmployeeDocumentRepository>();
        repository.ListAsync(document.EmployeeId, Arg.Any<CancellationToken>()).Returns([document]);

        EmployeeDocumentResult result = (await new ListEmployeeDocumentsQueryHandler(repository)
            .HandleAsync(new ListEmployeeDocumentsQuery(document.EmployeeId)))[0];

        result.DocumentType.Should().Be("Contract");
        result.ContentSha256.Should().Be(new string('b', 64));
        result.Should().NotBeNull();
        result.GetType().GetProperty("BlobKey").Should().BeNull();
    }
}
