using VumaRetail.Domain.Crm;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Crm;

/// <summary>
/// Lead-to-customer conversion: atomic, one-way, source of the customer UUID.
/// Scaffolding tests for Stage 19.
/// </summary>
public sealed class LeadConversionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Converting_a_new_lead_creates_a_customer_with_matching_identity()
    {
        var lead = new Lead(Guid.NewGuid(), "Athoi", "Molefe", "athoi@example.co.za",
            "0821234567", null, LeadSource.Web);
        var result = lead.Convert(Now);
        result.CustomerId.Should().NotBeEmpty();
        result.LeadStatus.Should().Be(LeadStatus.Converted);
        result.ConvertedAt.Should().Be(Now);
    }

    [Fact]
    public void Converting_an_already_converted_lead_throws()
    {
        var lead = new Lead(Guid.NewGuid(), "Sipho", "Khumalo", "s@example.co.za",
            null, null, LeadSource.WalkIn);
        lead.MarkConverted();
        Action act = () => lead.Convert(Now);
        act.Should().Throw<LeadAlreadyConvertedException>();
    }

    [Fact]
    public void Converted_lead_sets_Status_and_ConvertedAt()
    {
        var lead = new Lead(Guid.NewGuid(), "Thabo", "Dlamini", "t@example.co.za",
            null, null, LeadSource.Referral);
        Assert.Null(lead.ConvertedAt);
        lead.Convert(Now);
        lead.Status.Should().Be(LeadStatus.Converted);
        lead.ConvertedAt.Should().Be(Now);
    }
}
