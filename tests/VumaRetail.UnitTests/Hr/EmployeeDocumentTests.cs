using VumaRetail.Domain.HrManagement;

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
}
