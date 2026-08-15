using VumaRetail.Domain.Partners;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Partners;

public sealed class PartnerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();

    [Fact]
    public void A_partner_code_is_upper_cased()
    {
        Partner partner = Partner.Create(TenantId, "acme-co", "Acme & Co", PartnerType.Supplier);

        partner.Code.Should().Be("ACME-CO");
    }

    [Fact]
    public void A_partner_can_be_a_customer_a_supplier_or_both()
    {
        Partner both = Partner.Create(TenantId, "DUAL", "Dual Trading", PartnerType.Customer | PartnerType.Supplier);

        both.Type.Should().Be(PartnerType.Customer | PartnerType.Supplier);
    }

    [Fact]
    public void A_partner_with_neither_customer_nor_supplier_is_refused()
    {
        Action creating = () => Partner.Create(TenantId, "NOBODY", "Nobody", PartnerType.None);

        creating.Should().Throw<PartnerRuleException>().Which.Code.Should().Be("PARTNER_TYPE_REQUIRED");
    }

    [Fact]
    public void A_partner_cannot_exist_without_a_tenant()
    {
        Action creating = () => Partner.Create(Guid.Empty, "ACME", "Acme", PartnerType.Supplier);

        creating.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_partner_can_record_a_structured_address()
    {
        Address address = Address.Create("1 Voortrekker Road", "Cape Town", "ZA");

        Partner partner = Partner.Create(TenantId, "ACME", "Acme", PartnerType.Supplier, address);

        partner.Address.Should().Be(address);
    }

    [Fact]
    public void Updating_details_changes_every_optional_field()
    {
        Partner partner = Partner.Create(TenantId, "ACME", "Acme", PartnerType.Supplier);
        Address address = Address.Create("1 Voortrekker Road", "Cape Town", "ZA");

        partner.SetDetails("Acme Proprietary", address, "billing@acme.example", "+27210000000", "4123456789");

        partner.Name.Should().Be("Acme Proprietary");
        partner.Address.Should().Be(address);
        partner.Email.Should().Be("billing@acme.example");
        partner.Phone.Should().Be("+27210000000");
        partner.TaxNumber.Should().Be("4123456789");
    }

    [Fact]
    public void Updating_details_can_clear_every_optional_field()
    {
        Partner partner = Partner.Create(
            TenantId,
            "ACME",
            "Acme",
            PartnerType.Supplier,
            Address.Create("1 Voortrekker Road", "Cape Town", "ZA"),
            "billing@acme.example",
            "+27210000000",
            "4123456789");

        partner.SetDetails("Acme", null, null, null, null);

        partner.Address.Should().BeNull();
        partner.Email.Should().BeNull();
        partner.Phone.Should().BeNull();
        partner.TaxNumber.Should().BeNull();
    }

    [Fact]
    public void Changing_type_to_none_is_refused()
    {
        Partner partner = Partner.Create(TenantId, "ACME", "Acme", PartnerType.Supplier);

        Action changing = () => partner.SetType(PartnerType.None);

        changing.Should().Throw<PartnerRuleException>();
    }

    [Fact]
    public void Changing_type_to_both_flags_is_allowed()
    {
        Partner partner = Partner.Create(TenantId, "ACME", "Acme", PartnerType.Supplier);

        partner.SetType(PartnerType.Customer | PartnerType.Supplier);

        partner.Type.Should().Be(PartnerType.Customer | PartnerType.Supplier);
    }

    [Fact]
    public void Deactivating_a_partner_keeps_its_record()
    {
        Partner partner = Partner.Create(TenantId, "ACME", "Acme", PartnerType.Supplier);

        partner.Deactivate();

        partner.IsActive.Should().BeFalse();
        partner.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void A_deactivated_partner_can_be_reactivated()
    {
        Partner partner = Partner.Create(TenantId, "ACME", "Acme", PartnerType.Supplier);
        partner.Deactivate();

        partner.Activate();

        partner.IsActive.Should().BeTrue();
    }
}
