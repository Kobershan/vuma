using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Registry;

/// <summary>Command-level rules for the trading group: validation and caller identity.</summary>
public sealed class TradingGroupCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Propose_refuses_a_self_link_before_it_reaches_the_service()
    {
        Guid company = UuidV7.NewGuid();
        var validator = new ProposeCompanyLinkCommandValidator();

        var result = validator.Validate(new ProposeCompanyLinkCommand(company, company, CompanyLinkScope.SharedFloor));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Suspend_needs_a_reason_but_not_ten_characters_while_revoke_does()
    {
        var suspendValidator = new SuspendCompanyLinkCommandValidator();
        var revokeValidator = new RevokeCompanyLinkCommandValidator();

        suspendValidator.Validate(new SuspendCompanyLinkCommand(UuidV7.NewGuid(), "Dispute.")).IsValid.Should().BeTrue();
        suspendValidator.Validate(new SuspendCompanyLinkCommand(UuidV7.NewGuid(), "")).IsValid.Should().BeFalse();
        revokeValidator.Validate(new RevokeCompanyLinkCommand(UuidV7.NewGuid(), "Too short")).IsValid.Should().BeFalse();
        revokeValidator.Validate(new RevokeCompanyLinkCommand(UuidV7.NewGuid(), "Fraud confirmed.")).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Accept_records_the_acting_company_not_a_body_parameter()
    {
        // Regression test: the first implementation passed Guid.Empty as the accepting company,
        // so every acceptance died in "Company is not part of this link."
        Guid actingCompany = UuidV7.NewGuid();
        var links = Substitute.For<ICompanyLinkService>();
        var companies = Substitute.For<ICompanyContext>();
        var principal = Substitute.For<IPrincipalAccessor>();
        var operators = Substitute.For<IOperatorContext>();

        companies.RequireCompany().Returns(actingCompany);
        principal.Principal.Returns("user:accepting");
        operators.LicenceFingerprint.Returns("fp-1");

        var handler = new AcceptCompanyLinkCommandHandler(links, companies, principal, operators);
        await handler.HandleAsync(new AcceptCompanyLinkCommand(UuidV7.NewGuid()));

        await links.Received(1).AcceptAsync(
            Arg.Any<Guid>(), actingCompany, "user:accepting", "fp-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Propose_requires_an_operator_before_touching_the_service()
    {
        var links = Substitute.For<ICompanyLinkService>();
        var operators = Substitute.For<IOperatorContext>();

        operators.RequireOperatorId().Returns(UuidV7.NewGuid());

        var handler = new ProposeCompanyLinkCommandHandler(links, operators);
        links.ProposeAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CompanyLinkScope>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(CompanyLink.Create(
                UuidV7.NewGuid(),
                UuidV7.NewGuid(),
                call.ArgAt<Guid>(0),
                call.ArgAt<Guid>(1),
                CompanyLinkScope.SharedFloor,
                Now)));

        Guid returned = await handler.HandleAsync(new ProposeCompanyLinkCommand(UuidV7.NewGuid(), UuidV7.NewGuid(), CompanyLinkScope.SharedFloor));

        returned.Should().NotBe(Guid.Empty);
        operators.Received(1).RequireOperatorId();
    }
}
