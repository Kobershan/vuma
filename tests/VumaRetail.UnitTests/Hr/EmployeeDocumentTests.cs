using VumaRetail.Domain.HrManagement;
using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Hr;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using Microsoft.Extensions.Configuration;
using VumaRetail.Infrastructure.Security;

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
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(Guid.NewGuid());
        document.AssignCompany(company.CompanyId.Value);
        var repository = Substitute.For<IEmployeeDocumentRepository>();
        repository.ListAsync(document.EmployeeId, Arg.Any<CancellationToken>()).Returns([document]);

        EmployeeDocumentResult result = (await new ListEmployeeDocumentsQueryHandler(repository, company)
            .HandleAsync(new ListEmployeeDocumentsQuery(document.EmployeeId)))[0];

        result.DocumentType.Should().Be("Contract");
        result.ContentSha256.Should().Be(new string('b', 64));
        result.Should().NotBeNull();
        result.GetType().GetProperty("BlobKey").Should().BeNull();
    }

    [Fact]
    public async Task Download_authorization_issues_a_short_lived_opaque_grant()
    {
        var document = EmployeeDocument.Record(Guid.NewGuid(), Guid.NewGuid(), "Contract", "private/key.pdf", new string('c', 64),
            new DateOnly(2026, 9, 30));
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(Guid.NewGuid());
        document.AssignCompany(company.CompanyId.Value);
        var repository = Substitute.For<IEmployeeDocumentRepository>();
        var authorizer = Substitute.For<IEmployeeDocumentDownloadAuthorizer>();
        var clock = Substitute.For<VumaRetail.Application.Abstractions.IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        authorizer.Create(document, Arg.Any<DateTimeOffset>()).Returns("opaque-grant");
        repository.FindAsync(document.EmployeeId, document.Id, Arg.Any<CancellationToken>()).Returns(document);

        EmployeeDocumentDownloadResult result = (await new AuthorizeEmployeeDocumentDownloadQueryHandler(repository, authorizer, company, clock)
            .HandleAsync(new AuthorizeEmployeeDocumentDownloadQuery(document.EmployeeId, document.Id)))!;

        result.Token.Should().Be("opaque-grant");
        result.ExpiresAtUtc.Should().Be(clock.UtcNow.AddMinutes(15));
    }

    [Fact]
    public void Download_grant_validation_rejects_tampering_and_expiry()
    {
        var configuration = new ConfigurationManager();
        configuration["Security:EmployeeDocumentDownloadKey"] = new string('k', 32);
        var authorizer = new EmployeeDocumentDownloadAuthorizer(configuration);
        var document = EmployeeDocument.Record(Guid.NewGuid(), Guid.NewGuid(), "Contract", "private/key.pdf", new string('d', 64));
        DateTimeOffset expiry = new(2026, 9, 13, 12, 15, 0, TimeSpan.Zero);
        string token = authorizer.Create(document, expiry);

        authorizer.Validate(token, document.Id, expiry.AddMinutes(-1)).Should().BeTrue();
        authorizer.Validate(token + "x", document.Id, expiry.AddMinutes(-1)).Should().BeFalse();
        authorizer.Validate(token, document.Id, expiry).Should().BeFalse();
    }

    [Fact]
    public async Task Recording_document_rejects_employee_from_another_active_company()
    {
        var employee = Employee.Create(Guid.NewGuid(), "E-100", "Ada", "Lovelace", DateTimeOffset.UtcNow, EmploymentType.Permanent);
        employee.AssignCompany(Guid.NewGuid());
        var employees = Substitute.For<IEmployeeRepository>();
        employees.FindAsync(employee.Id, Arg.Any<CancellationToken>()).Returns(employee);
        var documents = Substitute.For<IEmployeeDocumentRepository>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(employee.TenantId);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(Guid.NewGuid());

        await FluentActions.Invoking(() => new RecordEmployeeDocumentCommandHandler(employees, documents, tenant, company)
            .HandleAsync(new RecordEmployeeDocumentCommand(employee.Id, "Contract", "private/contract.pdf", new string('f', 64))))
            .Should().ThrowAsync<InvalidOperationException>();
        documents.DidNotReceive().Add(Arg.Any<EmployeeDocument>());
    }
}
