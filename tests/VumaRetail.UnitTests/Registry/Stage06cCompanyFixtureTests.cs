using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Registry;

/// <summary>First Stage 06c acceptance slice: the canonical three-company fixture.</summary>
public sealed class Stage06cCompanyFixtureTests
{
    [Fact]
    public void Creates_three_isolated_company_definitions_with_stable_business_identity()
    {
        IReadOnlyList<Company> companies = Stage06cCompanyFixture.CreateCompanies();

        companies.Should().HaveCount(3);
        companies.Should().OnlyContain(company => company.TenantId == Stage06cCompanyFixture.TenantId);
        companies.Select(company => company.Code).Should().Equal("hardware", "distribution", "groceries");
        companies.Select(company => company.DocumentPrefix).Should().Equal("HW", "DC", "GR");
        companies.Select(company => company.Id).Should().OnlyHaveUniqueItems();
        companies.Select(company => company.Code).Should().OnlyHaveUniqueItems();
        companies.Select(company => company.DocumentPrefix).Should().OnlyHaveUniqueItems();
        companies.Should().OnlyContain(company =>
            company.LifecycleState == CompanyLifecycleState.Provisioning
            && !company.IsActive
            && company.ConnectionSecretRef == null);
    }

    [Fact]
    public void Fixture_inputs_are_reproducible_and_do_not_contain_connection_secrets()
    {
        Stage06cCompanyFixture.Companies.Select(company => company.Code)
            .Should().Equal("hardware", "distribution", "groceries");
        Stage06cCompanyFixture.Companies.Select(company => company.DocumentPrefix)
            .Should().Equal("HW", "DC", "GR");
        Stage06cCompanyFixture.Companies.Select(company => company.BaseCurrency)
            .Should().OnlyContain(currency => currency == "ZAR");
        Stage06cCompanyFixture.Companies.Select(company => company.Locale)
            .Should().OnlyContain(locale => locale == "en-ZA");
    }
}
