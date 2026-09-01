using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Sales.Pricing;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;

namespace VumaRetail.UnitTests.Sales;

/// <summary>
/// Which list wins, what the money does, and the two ways resolution is allowed to refuse.
/// </summary>
/// <remarks>
/// The rounding cases are the reason this file exists. §4.14 — found in Stage 09's review — was a unit
/// price rounded to the currency's scale and then multiplied by a quantity, which overcharges every
/// weighed line by up to half a cent. Business rule 2 is the fix, and the assertions below are what
/// stops it coming back: the resolver hands out a full-precision unit price and a whole-line discount,
/// and the single rounding happens on the extended amount.
/// </remarks>
public sealed class PriceResolverTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateOnly Friday = new(2026, 8, 14);
    private static readonly TimeOnly Midday = new(12, 0);

    [Fact]
    public async Task A_price_comes_back_with_the_list_it_came_off_and_an_explanation()
    {
        PriceResolver resolver = Resolver([RetailList(12.99m)], []);

        PriceResolution resolution = await resolver.ResolveAsync(Request(quantity: 1m));

        resolution.PriceListCode.Should().Be("RETAIL");
        resolution.UnitPrice.Amount.Should().Be(12.99m);
        resolution.NetPayable.Amount.Should().Be(12.99m);
        resolution.Explanation.Should().Contain("RETAIL").And.Contain("No promotion applied.");
    }

    [Fact]
    public async Task Rounding_happens_once_on_the_extended_amount_and_never_on_the_unit_price()
    {
        // R1.0050 × 3. Rounding the unit price first gives 1.01 × 3 = R3.03; rounding the extended
        // amount gives 3.0150 → R3.02. The second is correct and is the cent §4.14 was about.
        PriceResolver resolver = Resolver([RetailList(1.005m)], []);

        PriceResolution resolution = await resolver.ResolveAsync(Request(quantity: 3m));

        resolution.UnitPrice.Amount.Should().Be(1.0050m, "the unit price leaves at full precision");
        resolution.ExtendedPrice.Amount.Should().Be(3.0150m);
        resolution.NetPayable.Amount.Should().Be(3.02m);
    }

    [Fact]
    public async Task A_midpoint_rounds_away_from_zero_the_way_a_till_receipt_does()
    {
        // ADR-033. R2.5 × 2.001 = 5.0025 → R5.00; R2.5 × 2.002 = 5.005 → R5.01, not R5.00.
        PriceResolver resolver = Resolver([RetailList(2.5m)], []);

        (await resolver.ResolveAsync(Request(quantity: 2.001m))).NetPayable.Amount.Should().Be(5.00m);
        (await resolver.ResolveAsync(Request(quantity: 2.002m))).NetPayable.Amount.Should().Be(5.01m);
    }

    [Fact]
    public async Task A_weighed_quantity_keeps_its_six_places_through_the_extended_price()
    {
        // 2.456 kg of something at R89.99 — the deli-counter case Quantity's decimal(18,6) exists for.
        PriceResolver resolver = Resolver([RetailList(89.99m)], []);

        PriceResolution resolution = await resolver.ResolveAsync(Request(quantity: 2.456m));

        resolution.ExtendedPrice.Amount.Should().Be(221.0154m);
        resolution.NetPayable.Amount.Should().Be(221.02m);
    }

    [Fact]
    public async Task The_quantity_break_at_or_below_what_is_being_sold_wins()
    {
        // R12.99 each, R11.50 from a dozen. One below the break, exactly on it, one above.
        PriceList list = RetailList(12.99m);
        list.AddLine(ItemId, null, new Money(11.50m, "ZAR"), 12m);

        PriceResolver resolver = Resolver([list], []);

        (await resolver.ResolveAsync(Request(quantity: 11m))).UnitPrice.Amount.Should().Be(12.99m);
        (await resolver.ResolveAsync(Request(quantity: 12m))).UnitPrice.Amount.Should().Be(11.50m);
        (await resolver.ResolveAsync(Request(quantity: 13m))).UnitPrice.Amount.Should().Be(11.50m);
    }

    [Fact]
    public async Task A_store_scoped_list_beats_a_tenant_wide_one_whatever_their_priorities_say()
    {
        // A branch running its own prices set them for a local reason; head office raising the priority
        // of a national list should not silently override that.
        PriceList national = List("NATIONAL", 12.99m, storeId: null, priority: 100);
        PriceList branch = List("BRANCH", 9.99m, storeId: StoreId, priority: 0);

        PriceResolver resolver = Resolver([national, branch], []);

        PriceResolution resolution = await resolver.ResolveAsync(Request(quantity: 1m));

        resolution.PriceListCode.Should().Be("BRANCH");
        resolution.UnitPrice.Amount.Should().Be(9.99m);
    }

    [Fact]
    public async Task A_higher_priority_list_that_does_not_price_the_item_does_not_block_a_lower_one()
    {
        // Otherwise adding a promotional list would silently un-price everything it omitted.
        PriceList empty = PriceList.Create(
            TenantId, null, "EMPTY", "Prices nothing", "ZAR", PriceListKind.Retail,
            pricesIncludeTax: true, priority: 90, Friday.AddDays(-30), null);

        PriceResolver resolver = Resolver([empty, RetailList(12.99m)], []);

        (await resolver.ResolveAsync(Request(quantity: 1m))).PriceListCode.Should().Be("RETAIL");
    }

    [Fact]
    public async Task A_list_in_another_currency_is_refused_rather_than_converted()
    {
        // Business rule 10, and §4.13's defect answered one layer earlier: a code the caller can act on,
        // never an unhandled exception that bricks the terminal.
        PriceList dollars = PriceList.Create(
            TenantId, null, "USD", "Dollar list", "USD", PriceListKind.Retail,
            pricesIncludeTax: false, priority: 0, Friday.AddDays(-30), null);

        dollars.AddLine(ItemId, null, new Money(9.99m, "USD"), 1m);

        PriceResolver resolver = Resolver([dollars], []);

        Func<Task> resolving = () => resolver.ResolveAsync(Request(quantity: 1m));

        (await resolving.Should().ThrowAsync<SalesRuleException>())
            .Which.Code.Should().Be("SALES_CURRENCY_MISMATCH");
    }

    [Fact]
    public async Task Nothing_pricing_the_item_is_a_not_found_rather_than_a_zero()
    {
        // A price of zero and no price at all are different facts, and a till that silently rang up the
        // first when it meant the second is how stock leaves a shop for free.
        PriceResolver resolver = Resolver([], []);

        Func<Task> resolving = () => resolver.ResolveAsync(Request(quantity: 1m));

        (await resolving.Should().ThrowAsync<SalesNotFoundException>())
            .Which.Kind.Should().Be(DomainProblemKind.NotFound);
    }

    [Fact]
    public async Task A_promotion_reduces_the_line_without_touching_the_unit_price_it_hands_back()
    {
        // The pair POS's AddSaleLineCommand already takes: a unit price it stores as sold, and a
        // whole-line discount. That is what keeps ADR-072 true — Stage 10 changed what fills the field
        // in, not the field.
        Promotion special = Promotion.PercentageOff(
            TenantId, null, "TEN-OFF", "10% off", 10m, Friday.AddDays(-1), null);

        PriceResolver resolver = Resolver([RetailList(50m)], [special]);

        PriceResolution resolution = await resolver.ResolveAsync(Request(quantity: 2m));

        resolution.UnitPrice.Amount.Should().Be(50m);
        resolution.DiscountAmount.Amount.Should().Be(10m);
        resolution.NetPayable.Amount.Should().Be(90m);
        resolution.Explanation.Should().Contain("TEN-OFF").And.Contain("10% off");
    }

    [Fact]
    public async Task Exactly_one_of_an_item_or_a_variant_must_be_named()
    {
        PriceResolver resolver = Resolver([RetailList(12.99m)], []);

        Func<Task> both = () => resolver.ResolveAsync(new PriceResolutionRequest(
            ItemId, UuidV7.NewGuid(), null, 1m, StoreId, Friday, Midday, "ZAR"));

        (await both.Should().ThrowAsync<SalesRuleException>())
            .Which.Code.Should().Be("SALES_EXACTLY_ONE_ITEM_OR_VARIANT");
    }

    private static PriceResolutionRequest Request(decimal quantity)
        => new(ItemId, null, null, quantity, StoreId, Friday, Midday, "ZAR");

    private static PriceList RetailList(decimal unitPrice)
        => List("RETAIL", unitPrice, storeId: null, priority: 0);

    private static PriceList List(string code, decimal unitPrice, Guid? storeId, int priority)
    {
        PriceList list = PriceList.Create(
            TenantId, storeId, code, code, "ZAR", PriceListKind.Retail,
            pricesIncludeTax: true, priority, Friday.AddDays(-30), null);

        list.AddLine(ItemId, null, new Money(unitPrice, "ZAR"), 1m);

        return list;
    }

    private static PriceResolver Resolver(
        IReadOnlyList<PriceList> lists, IReadOnlyList<Promotion> promotions)
        => new(new StubPriceLists(lists), new StubPromotions(promotions));

    /// <summary>
    /// Returns the lists it was given, unfiltered.
    /// </summary>
    /// <remarks>
    /// Deliberately does no date, store or item filtering of its own, so that these tests exercise the
    /// resolver's own <c>ChooseList</c> rather than a fake's idea of the SQL. The repository's filtering
    /// is proven against real PostgreSQL in the integration suite, which is the only place it can be.
    /// </remarks>
    private sealed class StubPriceLists(IReadOnlyList<PriceList> lists) : IPriceListRepository
    {
        public Task<PriceList?> FindAsync(Guid priceListId, CancellationToken cancellationToken = default)
            => Task.FromResult(lists.FirstOrDefault(list => list.Id == priceListId));

        public Task<PriceList?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult(lists.FirstOrDefault(list =>
                string.Equals(list.Code, code, StringComparison.OrdinalIgnoreCase)));

        public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult(lists.Any(list =>
                string.Equals(list.Code, code, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<PriceList>> ListAsync(
            bool includeInactive, CancellationToken cancellationToken = default)
            => Task.FromResult(lists);

        public Task<IReadOnlyList<PriceList>> ListCandidatesAsync(
            Guid? itemId,
            Guid? itemVariantId,
            Guid? storeId,
            DateOnly onDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult(lists);

        public void Add(PriceList priceList)
        {
        }
    }

    private sealed class StubPromotions(IReadOnlyList<Promotion> promotions) : IPromotionRepository
    {
        public Task<Promotion?> FindAsync(Guid promotionId, CancellationToken cancellationToken = default)
            => Task.FromResult(promotions.FirstOrDefault(promotion => promotion.Id == promotionId));

        public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult(promotions.Any(promotion =>
                string.Equals(promotion.Code, code, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<Promotion>> ListLiveAsync(
            Guid? storeId, DateOnly onDate, CancellationToken cancellationToken = default)
            => Task.FromResult(promotions);

        public Task<IReadOnlyList<Promotion>> ListAsync(
            bool includeInactive, CancellationToken cancellationToken = default)
            => Task.FromResult(promotions);

        public void Add(Promotion promotion)
        {
        }
    }
}
