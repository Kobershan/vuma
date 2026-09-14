using Microsoft.Extensions.Configuration;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Assets;
using VumaRetail.Domain.Assets;
using VumaRetail.Infrastructure.Security;

namespace VumaRetail.UnitTests.Assets;

public sealed class ChecklistEvidenceTests
{
    [Fact]
    public async Task Evidence_authorization_is_company_scoped_and_short_lived()
    {
        Guid companyId = Guid.NewGuid();
        ChecklistExecution execution = ChecklistExecution.Submit(Guid.NewGuid(), Guid.NewGuid(), companyId,
            Guid.NewGuid(), Guid.NewGuid(), "device-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "private/evidence.jpg");
        IChecklistRepository repository = Substitute.For<IChecklistRepository>();
        repository.FindExecutionAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(execution.TenantId);
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        var authorizer = Substitute.For<IChecklistEvidenceAuthorizer>();
        authorizer.Create(execution, Arg.Any<DateTimeOffset>()).Returns("opaque-evidence-grant");

        ChecklistEvidenceDownloadResult result = (await new AuthorizeChecklistEvidenceDownloadQueryHandler(repository, authorizer, tenant, company, clock)
            .HandleAsync(new AuthorizeChecklistEvidenceDownloadQuery(execution.Id)))!;

        result.EvidenceReference.Should().Be("private/evidence.jpg");
        result.Token.Should().Be("opaque-evidence-grant");
        result.ExpiresAtUtc.Should().Be(clock.UtcNow.AddMinutes(15));
    }

    [Fact]
    public async Task Evidence_authorization_rejects_another_tenant()
    {
        ChecklistExecution execution = ChecklistExecution.Submit(Guid.NewGuid(), null, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "device-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "private/evidence.jpg");
        IChecklistRepository repository = Substitute.For<IChecklistRepository>();
        repository.FindExecutionAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(execution);
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(execution.CompanyId);
        var authorizer = Substitute.For<IChecklistEvidenceAuthorizer>();
        var clock = Substitute.For<IClock>();

        (await new AuthorizeChecklistEvidenceDownloadQueryHandler(repository, authorizer, tenant, company, clock)
            .HandleAsync(new AuthorizeChecklistEvidenceDownloadQuery(execution.Id))).Should().BeNull();
        authorizer.DidNotReceiveWithAnyArgs().Create(default!, default);
    }

    [Fact]
    public void Evidence_grant_validation_rejects_tampering_and_expiry()
    {
        var configuration = new ConfigurationManager();
        configuration["Security:ChecklistEvidenceDownloadKey"] = new string('e', 32);
        var authorizer = new ChecklistEvidenceAuthorizer(configuration);
        ChecklistExecution execution = ChecklistExecution.Submit(Guid.NewGuid(), null, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "device-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "private/evidence.jpg");
        DateTimeOffset expiry = new(2026, 9, 14, 12, 15, 0, TimeSpan.Zero);
        string token = authorizer.Create(execution, expiry);

        authorizer.Validate(token, execution.Id, expiry.AddMinutes(-1)).Should().BeTrue();
        authorizer.Validate(token + "x", execution.Id, expiry.AddMinutes(-1)).Should().BeFalse();
        authorizer.Validate(token, execution.Id, expiry).Should().BeFalse();
    }
}
