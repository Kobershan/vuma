using VumaRetail.Domain.Platform;

namespace VumaRetail.UnitTests.Platform;

public sealed class TenantTests
{
    [Fact]
    public void A_new_tenant_carries_the_South_African_deployment_defaults()
    {
        // CLAUDE.md §9: these are configuration, not hard-code. This test asserts the seed values;
        // SetLocalisation proves they are changeable.
        Tenant tenant = Tenant.CreateWithSouthAfricanDefaults("Vuma Foods (Pty) Ltd", "Vuma Foods");

        tenant.Locale.Should().Be("en-ZA");
        tenant.BaseCurrency.Should().Be("ZAR");
        tenant.TimeZone.Should().Be("Africa/Johannesburg");
    }

    [Fact]
    public void A_tenant_is_the_root_of_its_own_isolation_boundary()
    {
        // Its TenantId is its own Id, so the global query filter treats the tenant row exactly like
        // every other row instead of needing a special case that somebody later gets wrong.
        Tenant tenant = Tenant.CreateWithSouthAfricanDefaults("Vuma Foods (Pty) Ltd", "Vuma Foods");

        tenant.TenantId.Should().Be(tenant.Id);
    }

    [Fact]
    public void A_new_tenant_cannot_trade_until_it_is_activated()
    {
        Tenant tenant = Tenant.CreateWithSouthAfricanDefaults("Vuma Foods (Pty) Ltd", "Vuma Foods");

        tenant.IsActive.Should().BeFalse();

        tenant.Activate();

        tenant.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Every_localisation_default_is_changeable()
    {
        Tenant tenant = Tenant.CreateWithSouthAfricanDefaults("Vuma Foods (Pty) Ltd", "Vuma Foods");

        tenant.SetLocalisation("en-GB", "gbp", "Europe/London");

        tenant.Locale.Should().Be("en-GB");
        tenant.BaseCurrency.Should().Be("GBP");
        tenant.TimeZone.Should().Be("Europe/London");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_tenant_needs_a_legal_name(string legalName)
    {
        Action creating = () => Tenant.CreateWithSouthAfricanDefaults(legalName, "Vuma Foods");

        creating.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("ZA")]
    [InlineData("RAND")]
    [InlineData("Z4R")]
    public void A_tenant_cannot_report_in_something_that_is_not_a_currency(string currency)
    {
        Tenant tenant = Tenant.CreateWithSouthAfricanDefaults("Vuma Foods (Pty) Ltd", "Vuma Foods");

        Action setting = () => tenant.SetLocalisation("en-ZA", currency, "Africa/Johannesburg");

        setting.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_tenant_outside_South_Africa_supplies_its_own_localisation()
    {
        Tenant tenant = Tenant.Create(
            "Vuma Foods Ltd", "Vuma Foods", "en-GB", "gbp", "Europe/London");

        tenant.Locale.Should().Be("en-GB");
        tenant.BaseCurrency.Should().Be("GBP");
        tenant.TimeZone.Should().Be("Europe/London");
        tenant.TenantId.Should().Be(tenant.Id);
    }

    [Fact]
    public void Deactivating_a_tenant_is_not_the_same_as_deleting_it()
    {
        // Nor is it read-only enforcement, which is ADR-028's and comes from the licence, not here.
        Tenant tenant = Tenant.CreateWithSouthAfricanDefaults("Vuma Foods (Pty) Ltd", "Vuma Foods");
        tenant.Activate();

        tenant.Deactivate();

        tenant.IsActive.Should().BeFalse();
        tenant.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void A_blank_tax_registration_number_is_stored_as_absent_rather_than_as_empty()
    {
        // "" and null mean different things to a VAT return, and a blank string is neither.
        Tenant tenant = Tenant.CreateWithSouthAfricanDefaults("Vuma Foods (Pty) Ltd", "Vuma Foods");

        tenant.SetTaxRegistrationNumber("   ");

        tenant.TaxRegistrationNumber.Should().BeNull();
    }
}
