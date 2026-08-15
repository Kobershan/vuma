using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Partners.Commands;
using VumaRetail.Application.Partners.Queries;
using VumaRetail.Domain.Partners;
using VumaRetail.Domain.Primitives;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Partners;

/// <summary>
/// The Stage 06 <c>partners</c> command handlers against real PostgreSQL and real migrations
/// (<c>docs/TESTING.md</c> §2).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PartnerCommandTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Creating_a_partner_stores_an_upper_cased_code_and_its_address()
    {
        await using PartnerHarness harness = await PartnerHarness.CreateAsync(fixture);
        Address address = Address.Create("1 Voortrekker Road", "Cape Town", "ZA");

        Guid id = await harness.SendAsync(
            new CreatePartnerCommand("acme-co", "Acme & Co", PartnerType.Supplier, address, "billing@acme.example"));

        Partner partner = (await harness.Partners.FindAsync(id))!;
        partner.Code.Should().Be("ACME-CO");
        partner.Address.Should().Be(address);
        partner.TenantId.Should().Be(harness.TenantId);
    }

    [Fact]
    public async Task Refuses_a_second_partner_with_the_same_code()
    {
        await using PartnerHarness harness = await PartnerHarness.CreateAsync(fixture);
        await harness.SendAsync(new CreatePartnerCommand("ACME", "Acme", PartnerType.Supplier));

        Func<Task> again = () => harness.SendAsync(new CreatePartnerCommand("acme", "Acme Again", PartnerType.Customer));

        (await again.Should().ThrowAsync<PartnerConflictException>())
            .Which.Code.Should().Be("PARTNER_CODE_TAKEN");
    }

    [Fact]
    public async Task A_partner_with_neither_customer_nor_supplier_is_refused_through_the_pipeline()
    {
        await using PartnerHarness harness = await PartnerHarness.CreateAsync(fixture);

        Func<Task> creating = () => harness.SendAsync(new CreatePartnerCommand("NOBODY", "Nobody", PartnerType.None));

        await creating.Should().ThrowAsync<ValidationFailedException>();
    }

    [Fact]
    public async Task Updating_details_persists_the_change()
    {
        await using PartnerHarness harness = await PartnerHarness.CreateAsync(fixture);
        Guid id = await harness.SendAsync(new CreatePartnerCommand("ACME", "Acme", PartnerType.Supplier));

        await harness.SendAsync(new UpdatePartnerDetailsCommand(id, "Acme Proprietary", Email: "new@acme.example"));

        Partner partner = (await harness.Partners.FindAsync(id))!;
        partner.Name.Should().Be("Acme Proprietary");
        partner.Email.Should().Be("new@acme.example");
    }

    [Fact]
    public async Task Deactivating_a_partner_does_not_delete_it()
    {
        await using PartnerHarness harness = await PartnerHarness.CreateAsync(fixture);
        Guid id = await harness.SendAsync(new CreatePartnerCommand("ACME", "Acme", PartnerType.Supplier));

        await harness.SendAsync(new DeactivatePartnerCommand(id));

        Partner partner = (await harness.Partners.FindAsync(id))!;
        partner.IsActive.Should().BeFalse();
        partner.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Listing_partners_returns_a_stable_keyset_page_across_a_concurrent_insert()
    {
        await using PartnerHarness harness = await PartnerHarness.CreateAsync(fixture);

        for (int index = 0; index < 5; index++)
        {
            await harness.SendAsync(new CreatePartnerCommand($"PARTNER-{index:00}", $"Partner {index:00}", PartnerType.Supplier));
        }

        PageResult<PartnerResult> firstPage = await harness.QueryAsync(new ListPartnersQuery(Limit: 2));
        firstPage.Items.Should().HaveCount(2);
        firstPage.HasMore.Should().BeTrue();

        await harness.SendAsync(new CreatePartnerCommand("PARTNER-00A", "Inserted mid-page", PartnerType.Customer));

        PageResult<PartnerResult> secondPage = await harness.QueryAsync(
            new ListPartnersQuery(Limit: 2, After: firstPage.NextCursor));

        secondPage.Items.Select(partner => partner.Code)
            .Should().NotIntersectWith(firstPage.Items.Select(partner => partner.Code));
    }
}
