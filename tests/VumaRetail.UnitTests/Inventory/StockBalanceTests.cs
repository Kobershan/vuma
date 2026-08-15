using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Inventory;

/// <summary>
/// The weighted-average moving cost arithmetic (ADR-068) — the one piece of Stage 08 where a rounding
/// or ordering mistake produces a plausible-looking number rather than an obvious failure.
/// </summary>
public sealed class StockBalanceTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();

    private static StockBalance NewBalance() =>
        StockBalance.Open(TenantId, StoreId, LocationId, ItemId, null, "EA", "ZAR");

    [Fact]
    public void A_new_balance_opens_empty_in_the_unit_and_currency_it_was_given()
    {
        StockBalance balance = NewBalance();

        balance.QuantityOnHand.Value.Should().Be(0m);
        balance.QuantityOnHand.UnitOfMeasure.Should().Be("EA");
        balance.AverageCost.Amount.Should().Be(0m);
        balance.AverageCost.Currency.Should().Be("ZAR");
    }

    [Fact]
    public void The_first_receipt_sets_the_average_to_its_own_cost()
    {
        // A brand-new balance has no prior value to blend, so the weighted average collapses to the
        // receipt's own unit cost rather than averaging against a meaningless zero.
        StockBalance balance = NewBalance();

        balance.ApplyReceipt(new Quantity(10m, "EA"), new Money(25m, "ZAR"));

        balance.QuantityOnHand.Value.Should().Be(10m);
        balance.AverageCost.Amount.Should().Be(25m);
        balance.TotalValue.Amount.Should().Be(250m);
    }

    [Fact]
    public void A_second_receipt_at_a_different_cost_blends_into_the_running_average()
    {
        // 10 @ 20 = 200, then 10 @ 30 = 300. Total 500 over 20 units = 25.
        StockBalance balance = NewBalance();

        balance.ApplyReceipt(new Quantity(10m, "EA"), new Money(20m, "ZAR"));
        balance.ApplyReceipt(new Quantity(10m, "EA"), new Money(30m, "ZAR"));

        balance.QuantityOnHand.Value.Should().Be(20m);
        balance.AverageCost.Amount.Should().Be(25m);
        balance.TotalValue.Amount.Should().Be(500m);
    }

    [Fact]
    public void An_issue_relieves_at_the_average_without_changing_it()
    {
        // The defining property of weighted average: issuing values stock out at cost, it does not
        // revalue what remains. Getting this wrong is how a costing engine slowly drifts.
        StockBalance balance = NewBalance();
        balance.ApplyReceipt(new Quantity(10m, "EA"), new Money(20m, "ZAR"));
        balance.ApplyReceipt(new Quantity(10m, "EA"), new Money(30m, "ZAR"));

        Money relieved = balance.ApplyIssue(new Quantity(5m, "EA"));

        relieved.Amount.Should().Be(125m);
        balance.QuantityOnHand.Value.Should().Be(15m);
        balance.AverageCost.Amount.Should().Be(25m);
    }

    [Fact]
    public void A_receipt_after_an_issue_blends_against_what_is_left_not_what_was_ever_received()
    {
        StockBalance balance = NewBalance();
        balance.ApplyReceipt(new Quantity(10m, "EA"), new Money(20m, "ZAR"));
        balance.ApplyIssue(new Quantity(5m, "EA"));

        // 5 left @ 20 = 100, plus 5 @ 40 = 200. Total 300 over 10 = 30.
        balance.ApplyReceipt(new Quantity(5m, "EA"), new Money(40m, "ZAR"));

        balance.QuantityOnHand.Value.Should().Be(10m);
        balance.AverageCost.Amount.Should().Be(30m);
    }

    [Fact]
    public void Issuing_everything_leaves_a_zero_balance_that_still_remembers_its_cost()
    {
        StockBalance balance = NewBalance();
        balance.ApplyReceipt(new Quantity(10m, "EA"), new Money(20m, "ZAR"));

        balance.ApplyIssue(new Quantity(10m, "EA"));

        balance.QuantityOnHand.Value.Should().Be(0m);
        balance.TotalValue.Amount.Should().Be(0m);
        // The average survives the balance emptying, so a positive adjustment or a found-stock
        // variance afterwards has a cost basis to value itself at rather than throwing.
        balance.AverageCost.Amount.Should().Be(20m);
    }

    [Fact]
    public void Stock_cannot_be_issued_below_zero()
    {
        StockBalance balance = NewBalance();
        balance.ApplyReceipt(new Quantity(3m, "EA"), new Money(20m, "ZAR"));

        Action issuing = () => balance.ApplyIssue(new Quantity(4m, "EA"));

        issuing.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_INSUFFICIENT_STOCK");
    }

    [Fact]
    public void A_receipt_in_a_different_unit_of_measure_is_refused()
    {
        // Adding kilograms to a balance held in units is the quantity equivalent of adding rands to
        // dollars — refused rather than silently coerced.
        StockBalance balance = NewBalance();

        Action receiving = () => balance.ApplyReceipt(new Quantity(1m, "KG"), new Money(20m, "ZAR"));

        receiving.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_UOM_MISMATCH");
    }

    [Fact]
    public void A_receipt_in_a_different_currency_is_refused()
    {
        StockBalance balance = NewBalance();

        Action receiving = () => balance.ApplyReceipt(new Quantity(1m, "EA"), new Money(20m, "USD"));

        receiving.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_CURRENCY_MISMATCH");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_receipt_must_carry_a_positive_quantity(decimal quantity)
    {
        StockBalance balance = NewBalance();

        Action receiving = () => balance.ApplyReceipt(new Quantity(quantity, "EA"), new Money(20m, "ZAR"));

        receiving.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_QUANTITY_MUST_BE_POSITIVE");
    }

    [Fact]
    public void A_balance_names_exactly_one_of_an_item_or_a_variant()
    {
        Action both = () => StockBalance.Open(TenantId, StoreId, LocationId, ItemId, UuidV7.NewGuid(), "EA", "ZAR");
        Action neither = () => StockBalance.Open(TenantId, StoreId, LocationId, null, null, "EA", "ZAR");

        both.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_EXACTLY_ONE_ITEM_OR_VARIANT");
        neither.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_EXACTLY_ONE_ITEM_OR_VARIANT");
    }

    [Fact]
    public void An_average_that_does_not_divide_evenly_keeps_its_precision()
    {
        // 1 @ 10 and 2 @ 20 is 50 over 3 = 16.6666… Money holds four decimal places (§7 rule 4), so
        // the average keeps enough precision that re-extending it does not visibly lose value.
        StockBalance balance = NewBalance();

        balance.ApplyReceipt(new Quantity(1m, "EA"), new Money(10m, "ZAR"));
        balance.ApplyReceipt(new Quantity(2m, "EA"), new Money(20m, "ZAR"));

        balance.AverageCost.Amount.Should().Be(16.6667m);
        balance.QuantityOnHand.Value.Should().Be(3m);
    }
}
