using System.Security.Claims;
using Microsoft.Extensions.Logging;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Identity;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.Infrastructure.Security.Identity;

namespace VumaRetail.UnitTests.Registry;

/// <summary>Token company membership: serialisation, the 403 rule and the ambient operator.</summary>
public sealed class OperatorClaimsTests
{
    [Fact]
    public void Companies_claim_round_trips_and_absent_means_unrestricted()
    {
        Guid companyA = UuidV7.NewGuid();
        Guid companyB = UuidV7.NewGuid();
        var memberships = new List<CompanyMembership>
        {
            new(companyA, "Manager"),
            new(companyB, "Cashier"),
        };

        ClaimsPrincipal user = PrincipalWith(VumaCompanyClaims.Serialise(memberships));

        IReadOnlyList<CompanyMembership>? parsed = VumaCompanyClaims.Parse(user);

        parsed.Should().HaveCount(2);
        parsed.Should().Contain(m => m.CompanyId == companyA && m.Roles == "Manager");

        // Both carried companies pass; a stranger is refused with the 403 refusal.
        var carried = () => VumaCompanyClaims.RequireCompany(user, companyB);
        carried.Should().NotThrow();

        Guid strangerId = UuidV7.NewGuid();
        var stranger = () => VumaCompanyClaims.RequireCompany(user, strangerId);
        stranger.Should().Throw<ForbiddenException>().Where(e => e.CompanyId == strangerId);
    }

    [Fact]
    public void RequireCompany_allows_tokens_without_a_companies_claim()
    {
        // Pre-registry logins and system principals predate the rule.
        var legacy = () => VumaCompanyClaims.RequireCompany(new ClaimsPrincipal(), UuidV7.NewGuid());
        legacy.Should().NotThrow();

        var empty = () => VumaCompanyClaims.RequireCompany(PrincipalWith("[]"), UuidV7.NewGuid());
        empty.Should().NotThrow();
    }

    [Fact]
    public void Operator_context_requires_a_present_active_operator()
    {
        var principal = Substitute.For<IPrincipalAccessor>();
        principal.Principal.Returns("user:owner");
        var context = new OperatorContext(Substitute.For<ILogger<OperatorContext>>(), principal);

        var missing = () => context.RequireOperatorId();
        missing.Should().Throw<InvalidOperationException>();

        context.SetOperator(UuidV7.NewGuid(), "Op", "fp", isActive: false);

        var lapsed = () => context.RequireOperatorId();
        lapsed.Should().Throw<InvalidOperationException>();

        Guid operatorId = UuidV7.NewGuid();
        context.SetOperator(operatorId, "Op", "fp", isActive: true);

        context.RequireOperatorId().Should().Be(operatorId);
        context.LicenceFingerprint.Should().Be("fp");
        context.Principal.Should().Be("user:owner");
    }

    private static ClaimsPrincipal PrincipalWith(string companiesClaim)
    {
        var identity = new ClaimsIdentity(
            [new Claim(VumaClaims.Companies, companiesClaim)],
            authenticationType: "test");

        return new ClaimsPrincipal(identity);
    }
}
