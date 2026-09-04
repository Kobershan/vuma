using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Registry;

/// <summary>The registry refusal contract: stable codes, statuses and problem shapes.</summary>
public sealed class RegistryExceptionsTests
{
    [Fact]
    public void Company_link_refusal_names_both_companies_and_the_scope()
    {
        Guid companyA = UuidV7.NewGuid();
        Guid companyB = UuidV7.NewGuid();

        var refusal = new CompanyLinkRequiredException(companyA, companyB, CompanyLinkScope.SharedCredit);

        refusal.Code.Should().Be("COMPANY_LINK_REQUIRED");
        refusal.Kind.Should().Be(DomainProblemKind.Forbidden);
        refusal.CompanyA.Should().Be(companyA);
        refusal.CompanyB.Should().Be(companyB);
        refusal.RequiredScope.Should().Be(CompanyLinkScope.SharedCredit);
        refusal.ProblemTypeUrl.Should().Be("https://vuma.dev/problems/company-link-required");
        refusal.ProblemExtensions["companyA"].Should().Be(companyA);
        refusal.ProblemExtensions["companyB"].Should().Be(companyB);
        refusal.ProblemExtensions["requiredScope"].Should().Be("SharedCredit");
    }

    [Fact]
    public void Off_token_company_is_a_403_not_a_404()
    {
        var refusal = new ForbiddenException(UuidV7.NewGuid());

        refusal.Code.Should().Be("COMPANY_NOT_IN_TOKEN");
        refusal.Kind.Should().Be(DomainProblemKind.Forbidden);
    }

    [Fact]
    public void Missing_rows_are_404_and_collisions_are_409()
    {
        var missing = new RegistryNotFoundException("company link", UuidV7.NewGuid());
        var collision = RegistryConflictException.LinkAlreadyExists();

        missing.Kind.Should().Be(DomainProblemKind.NotFound);
        missing.Code.Should().Be("REGISTRY_NOT_FOUND");
        collision.Kind.Should().Be(DomainProblemKind.Conflict);
        collision.Code.Should().Be("REGISTRY_LINK_EXISTS");
    }

    [Fact]
    public void Cross_operator_grants_are_rule_violations_with_stable_codes()
    {
        RegistryRuleException refusal = RegistryExceptions.DifferentOperatorsCannotLink(UuidV7.NewGuid(), UuidV7.NewGuid());

        refusal.Code.Should().Be("REGISTRY_DIFFERENT_OPERATORS");
        refusal.Kind.Should().Be(DomainProblemKind.Rule);
    }
}
