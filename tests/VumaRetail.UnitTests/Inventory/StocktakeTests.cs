using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Inventory;

/// <summary>
/// The stocktake session's one legal state transition, and the variance arithmetic finalizing posts.
/// </summary>
public sealed class StocktakeTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    private static StocktakeSession OpenSession() => StocktakeSession.Open(TenantId, StoreId, LocationId);

    private static StocktakeLine RecordLine(StocktakeSession session, decimal system, decimal counted)
        => StocktakeLine.Record(
            TenantId, StoreId, session.Id, ItemId, null,
            new Quantity(system, "EA"), new Quantity(counted, "EA"));

    [Fact]
    public void A_new_session_is_open()
    {
        StocktakeSession session = OpenSession();

        session.Status.Should().Be(StocktakeStatus.Open);
        session.IsFinalized.Should().BeFalse();
        session.FinalizedAt.Should().BeNull();
    }

    [Fact]
    public void Finalizing_closes_the_session_and_stamps_when()
    {
        StocktakeSession session = OpenSession();

        session.Finalize(Now);

        session.Status.Should().Be(StocktakeStatus.Finalized);
        session.IsFinalized.Should().BeTrue();
        session.FinalizedAt.Should().Be(Now);
    }

    [Fact]
    public void A_finalized_session_refuses_to_be_finalized_again()
    {
        // Double-finalizing would post every variance a second time, doubling the correction — which
        // is exactly the kind of mistake a retry after a timeout produces.
        StocktakeSession session = OpenSession();
        session.Finalize(Now);

        Action finalizing = () => session.Finalize(Now);

        finalizing.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_STOCKTAKE_ALREADY_FINALIZED");
    }

    [Fact]
    public void A_finalized_session_refuses_further_counts()
    {
        StocktakeSession session = OpenSession();
        session.Finalize(Now);

        Action counting = session.EnsureOpen;

        counting.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_STOCKTAKE_ALREADY_FINALIZED");
    }

    [Fact]
    public void A_line_reports_counted_minus_system_as_its_variance()
    {
        StocktakeSession session = OpenSession();

        RecordLine(session, system: 10m, counted: 8m).Variance.Value.Should().Be(-2m);
        RecordLine(session, system: 10m, counted: 12m).Variance.Value.Should().Be(2m);
        RecordLine(session, system: 10m, counted: 10m).Variance.Value.Should().Be(0m);
    }

    [Fact]
    public void A_recount_replaces_both_the_count_and_the_system_snapshot()
    {
        // The system quantity is re-snapshotted too: stock keeps moving while a count is in progress,
        // and a recount at 14:00 must be compared against what the system said at 14:00.
        StocktakeSession session = OpenSession();
        StocktakeLine line = RecordLine(session, system: 10m, counted: 8m);

        line.Recount(new Quantity(9m, "EA"), new Quantity(9m, "EA"));

        line.SystemQuantity.Value.Should().Be(9m);
        line.CountedQuantity.Value.Should().Be(9m);
        line.Variance.Value.Should().Be(0m);
    }

    [Fact]
    public void A_count_may_be_zero_but_not_negative()
    {
        // Counting nothing on the shelf is a real and important result; counting minus three is not.
        StocktakeSession session = OpenSession();

        Action zero = () => RecordLine(session, system: 10m, counted: 0m);
        Action negative = () => RecordLine(session, system: 10m, counted: -1m);

        zero.Should().NotThrow();
        negative.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_QUANTITY_MUST_BE_POSITIVE");
    }

    [Fact]
    public void A_recount_may_not_go_negative_either()
    {
        StocktakeSession session = OpenSession();
        StocktakeLine line = RecordLine(session, system: 10m, counted: 8m);

        Action recounting = () => line.Recount(new Quantity(10m, "EA"), new Quantity(-1m, "EA"));

        recounting.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_QUANTITY_MUST_BE_POSITIVE");
    }

    [Fact]
    public void A_session_must_belong_to_a_tenant_and_name_a_location()
    {
        Action noTenant = () => StocktakeSession.Open(Guid.Empty, StoreId, LocationId);
        Action noLocation = () => StocktakeSession.Open(TenantId, StoreId, Guid.Empty);

        noTenant.Should().Throw<ArgumentException>();
        noLocation.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_line_names_exactly_one_of_an_item_or_a_variant()
    {
        StocktakeSession session = OpenSession();

        Action neither = () => StocktakeLine.Record(
            TenantId, StoreId, session.Id, null, null,
            new Quantity(1m, "EA"), new Quantity(1m, "EA"));

        neither.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_EXACTLY_ONE_ITEM_OR_VARIANT");
    }
}
