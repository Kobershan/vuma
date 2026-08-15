using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Platform;

public sealed class StoreTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();

    [Fact]
    public void A_store_belongs_to_a_tenant_and_to_itself()
    {
        Store store = Store.Create(TenantId, "jhb01", "Johannesburg");

        store.TenantId.Should().Be(TenantId);
        store.StoreId.Should().Be(store.Id);
    }

    [Fact]
    public void A_store_code_is_upper_cased()
    {
        // The code appears on receipts and in document number series, so "jhb01" and "JHB01" must not
        // be able to become two stores.
        Store store = Store.Create(TenantId, "jhb01", "Johannesburg");

        store.Code.Should().Be("JHB01");
    }

    [Fact]
    public void A_store_cannot_exist_without_a_tenant()
    {
        Action creating = () => Store.Create(Guid.Empty, "JHB01", "Johannesburg");

        creating.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_store_starts_open()
    {
        Store.Create(TenantId, "JHB01", "Johannesburg").IsActive.Should().BeTrue();
    }

    [Fact]
    public void Closing_a_store_keeps_its_record()
    {
        // §7 rule 8. A closed store still owns years of sales history, so closing is a state change,
        // never a delete.
        Store store = Store.Create(TenantId, "JHB01", "Johannesburg");

        store.Close();

        store.IsActive.Should().BeFalse();
        store.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void A_closed_store_can_be_reopened()
    {
        Store store = Store.Create(TenantId, "JHB01", "Johannesburg");
        store.Close();

        store.Open();

        store.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Renaming_a_store_can_also_set_its_structured_address()
    {
        Store store = Store.Create(TenantId, "JHB01", "Johannesburg");
        Address address = Address.Create("12 Main Road", "Johannesburg", "ZA");

        store.SetDetails("Johannesburg Central", address);

        store.Name.Should().Be("Johannesburg Central");
        store.Address.Should().Be(address);
    }

    [Fact]
    public void Renaming_a_store_can_also_clear_its_address()
    {
        Store store = Store.Create(TenantId, "JHB01", "Johannesburg");
        store.SetDetails("Johannesburg", Address.Create("12 Main Road", "Johannesburg", "ZA"));

        store.SetDetails("Johannesburg Central", null);

        store.Name.Should().Be("Johannesburg Central");
        store.Address.Should().BeNull();
    }

    [Fact]
    public void A_store_inherits_the_tenants_localisation_unless_it_overrides_it()
    {
        Store store = Store.Create(TenantId, "GAB01", "Gaborone");

        store.CurrencyOverride.Should().BeNull();
        store.TimeZoneOverride.Should().BeNull();

        store.SetLocalisationOverrides("bwp", "Africa/Gaborone");

        store.CurrencyOverride.Should().Be("BWP");
        store.TimeZoneOverride.Should().Be("Africa/Gaborone");
    }

    [Fact]
    public void An_override_can_be_cleared_back_to_the_tenants_value()
    {
        Store store = Store.Create(TenantId, "GAB01", "Gaborone");
        store.SetLocalisationOverrides("BWP", "Africa/Gaborone");

        store.SetLocalisationOverrides(null, null);

        store.CurrencyOverride.Should().BeNull();
        store.TimeZoneOverride.Should().BeNull();
    }

    [Fact]
    public void A_store_cannot_trade_in_something_that_is_not_a_currency()
    {
        Store store = Store.Create(TenantId, "GAB01", "Gaborone");

        Action setting = () => store.SetLocalisationOverrides("PULA", null);

        setting.Should().Throw<ArgumentException>();
    }
}
