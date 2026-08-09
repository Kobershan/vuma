using ZenithRetail.Application.Abstractions;

namespace ZenithRetail.UnitTests.Abstractions;

/// <summary>
/// The attribute Stage 04b's read-only interceptor reads (ADR-034).
/// </summary>
/// <remarks>
/// The architecture tests assert that every command carries one. These assert that the attribute
/// itself behaves — in particular that its defaults are the safe ones, because a default that
/// silently means "not exempt" and "nobody said" is what makes the build break catchable.
/// </remarks>
public sealed class CommandSideEffectTests
{
    [Fact]
    public void An_attribute_reports_the_effect_it_was_given()
    {
        CommandSideEffectAttribute attribute = new(SideEffect.Write);

        attribute.Effect.Should().Be(SideEffect.Write);
    }

    [Fact]
    public void An_attribute_claims_no_exemption_unless_it_says_so()
    {
        // Every exemption is a hole in the commercial lever (ADR-028). Defaulting to None means a
        // new command has to opt into being a hole, in a diff somebody reviews.
        new CommandSideEffectAttribute(SideEffect.Write).Exemption.Should().Be(ReadOnlyExemption.None);
    }

    [Theory]
    [InlineData(ReadOnlyExemption.Payment)]
    [InlineData(ReadOnlyExemption.OfflineFlush)]
    [InlineData(ReadOnlyExemption.Backup)]
    public void The_three_ADR_028_carve_outs_can_be_declared(ReadOnlyExemption exemption)
    {
        CommandSideEffectAttribute attribute = new(SideEffect.Write) { Exemption = exemption };

        attribute.Exemption.Should().Be(exemption);
    }

    [Fact]
    public void Unclassified_is_the_zero_value_so_a_missing_attribute_is_detectable()
    {
        // If the default were ReadOnly, a forgotten attribute would ship a write that stays
        // permitted during a lapse. If it were Write, it would ship a query that gets blocked.
        default(SideEffect).Should().Be(SideEffect.Unclassified);
        default(ReadOnlyExemption).Should().Be(ReadOnlyExemption.None);
    }

    [Fact]
    public void Unit_has_exactly_one_value()
    {
        Unit.Value.Should().Be(default(Unit));
    }
}
