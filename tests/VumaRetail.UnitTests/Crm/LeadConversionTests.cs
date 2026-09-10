using VumaRetail.Domain.Crm;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Crm;

/// <summary>
/// Lead-to-customer conversion: atomic, one-way, source of the customer UUID.
/// Scaffolding tests for Stage 19.
/// </summary>
public sealed class LeadConversionTests
{
    [Fact]
    public void Converting_a_new_lead_creates_a_customer_with_matching_identity()
    {
        var lead = new Lead(Guid.NewGuid(), "Athoi", "Molefe", "athoi@example.co.za",
            "0821234567", null, LeadSource.Web);
        var result = lead.Convert();
        result.CustomerId.Should().NotBeEmpty();
        result.LeadStatus.Should().Be(LeadStatus.Converted);
        result.ConvertedAt.Should().NotBeNull();
    }

    [Fact]
    public void Converting_an_already_converted_lead_throws()
    {
        var lead = new Lead(Guid.NewGuid(), "Sipho", "Khumalo", "s@example.co.za",
            null, null, LeadSource.WalkIn);
        lead.MarkConverted();
        Action act = () => lead.Convert();
        act.Should().Throw<LeadAlreadyConvertedException>();
    }

    [Fact]
    public void Converted_lead_sets_Status_and_ConvertedAt()
    {
        var lead = new Lead(Guid.NewGuid(), "Thabo", "Dlamini", "t@example.co.za",
            null, null, LeadSource.Referral);
        Assert.Null(lead.ConvertedAt);
        lead.Convert();
        lead.Status.Should().Be(LeadStatus.Converted);
        lead.ConvertedAt.Should().NotBeNull();
    }
}
