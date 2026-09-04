using VumaRetail.Application.Identity;
using VumaRetail.Contracts.Registry;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Registry;

/// <summary>The trading-group API and token shapes construct with the documented fields.</summary>
public sealed class TradingGroupContractsTests
{
    [Fact]
    public void Responses_carry_every_documented_field()
    {
        Guid id = UuidV7.NewGuid();
        DateTimeOffset now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

        var link = new CompanyLinkResponse(id, UuidV7.NewGuid(), UuidV7.NewGuid(), 3, "Active", now, null);
        var premises = new PremisesResponse(id, "P1", "Name", true);
        var user = new RegistryUserResponse(id, "login", "Name", true);
        var membership = new CompanyMembership(id, "Manager");
        var subject = new TokenSubject(id, id, "Name", "stamp", null, null, id, [membership]);
        var enrichment = new TokenCompanyEnrichment(id, [membership]);

        link.Status.Should().Be("Active");
        link.Scopes.Should().Be(3);
        premises.Code.Should().Be("P1");
        user.Login.Should().Be("login");
        subject.OperatorId.Should().Be(id);
        subject.Companies.Should().ContainSingle().Which.Roles.Should().Be("Manager");
        enrichment.Companies.Should().ContainSingle();
        TokenCompanyEnrichment.Empty.OperatorId.Should().BeNull();
        TokenCompanyEnrichment.Empty.Companies.Should().BeEmpty();
    }

    [Fact]
    public void Requests_carry_every_documented_field()
    {
        Guid a = UuidV7.NewGuid();
        Guid b = UuidV7.NewGuid();

        new ProposeCompanyLinkRequest(a, b, 3).Scopes.Should().Be(3);
        new SuspendCompanyLinkRequest("Dispute.").Reason.Should().Be("Dispute.");
        new RevokeCompanyLinkRequest("Fraud confirmed.").Reason.Should().Contain("Fraud");
        new CreatePremisesRequest("P1", "Name", "Addr", "0,0", "9-5").Code.Should().Be("P1");
        new AddPremisesOccupancyRequest(a, b).StoreId.Should().Be(b);
        new CreateRegistryUserRequest("login", "Name", "contact", a).OperatorId.Should().Be(a);
        new GrantCompanyAccessRequest(a, "Cashier").Roles.Should().Be("Cashier");
        new RegisterTerminalRequest(a, "T1", new string('a', 64)).TerminalId.Should().Be("T1");
        new SetTerminalCompaniesRequest([a]).CompanyIds.Should().ContainSingle();
        new OperatorResponse(a, "Op", true, [new OperatorCompanyResponse(b, "HW", true)])
            .Companies.Should().ContainSingle();
    }
}
