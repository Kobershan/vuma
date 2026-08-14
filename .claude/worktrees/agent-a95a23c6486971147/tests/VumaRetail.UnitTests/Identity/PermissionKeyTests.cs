using VumaRetail.Domain.Identity;

namespace VumaRetail.UnitTests.Identity;

/// <summary>
/// <see cref="PermissionKey"/> — the shape of a permission, decided once (ADR-013).
/// </summary>
public sealed class PermissionKeyTests
{
    [Fact]
    public void Splits_a_permission_into_module_entity_and_action()
    {
        PermissionKey key = PermissionKey.Parse("inventory.stocktake.approve");

        key.Module.Should().Be("inventory");
        key.EntityName.Should().Be("stocktake");
        key.Action.Should().Be("approve");
        key.Value.Should().Be("inventory.stocktake.approve");
    }

    [Theory]
    [InlineData("inventory.stocktake")]
    [InlineData("inventory.stocktake.approve.now")]
    [InlineData("inventory..approve")]
    [InlineData(".stocktake.approve")]
    [InlineData("Inventory.Stocktake.Approve")]
    [InlineData("inventory.stock take.approve")]
    [InlineData("inventory.stocktake.approve!")]
    [InlineData("1nventory.stocktake.approve")]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_anything_that_is_not_module_entity_action(string candidate)
    {
        PermissionKey.TryParse(candidate, out _).Should().BeFalse();

        Action parse = () => PermissionKey.Parse(candidate);

        parse.Should().Throw<InvalidPermissionKeyException>()
            .Which.Code.Should().Be("PERMISSION_KEY_MALFORMED");
    }

    [Fact]
    public void Refuses_a_permission_longer_than_the_column_that_stores_it()
    {
        // Silently truncating at the database would turn two different permissions into one grant.
        string tooLong = $"module.entity.{new string('a', PermissionKey.MaxLength)}";

        PermissionKey.TryParse(tooLong, out _).Should().BeFalse();
    }

    [Fact]
    public void Allows_hyphens_inside_a_segment()
    {
        // Real permissions need them: `finance.credit-note.post`, `pos.cash-up.approve`.
        PermissionKey.TryParse("finance.credit-note.post", out PermissionKey key).Should().BeTrue();

        key.EntityName.Should().Be("credit-note");
    }

    [Fact]
    public void Compares_by_value_so_it_can_be_a_dictionary_key()
    {
        PermissionKey.Parse("identity.user.view").Should().Be(PermissionKey.Parse("identity.user.view"));
        PermissionKey.Parse("identity.user.view").Should().NotBe(PermissionKey.Parse("identity.user.create"));
    }
}
