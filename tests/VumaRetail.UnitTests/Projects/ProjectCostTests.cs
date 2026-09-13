using FluentAssertions;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Projects;

namespace VumaRetail.UnitTests.Projects;

public sealed class ProjectCostTests
{
    [Fact]
    public void Reversal_is_a_new_negative_entry_and_preserves_original()
    {
        ProjectCostEntry original = ProjectCostEntry.Record(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "TIMESHEET-1", ProjectCostKind.Labour, new Money(200m, "ZAR"));
        ProjectCostEntry reversal = ProjectCostEntry.Reverse(original, "REVERSAL-1");
        original.Amount.Amount.Should().Be(200m);
        reversal.Amount.Amount.Should().Be(-200m);
        reversal.ReversesEntryId.Should().Be(original.Id);
    }
}
