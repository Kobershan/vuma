using NSubstitute;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using VumaRetail.Finance.Queries;
using static VumaRetail.UnitTests.Finance.FinanceTestContext;

namespace VumaRetail.UnitTests.Finance;

/// <summary>
/// AR and AP ageing, bucketed as of a fixed date.
/// </summary>
/// <remarks>
/// Every assertion here pins <c>asOf</c> explicitly. An ageing report is a statement about a date,
/// so a test that let the wall clock supply it would change bucket a month after it was written —
/// which is exactly the failure the <c>IClock</c> injection in <c>FinanceEndpoints</c> exists to
/// prevent on the endpoint side.
/// </remarks>
public sealed class AgeingTests
{
    private static readonly DateOnly AsOf = new(2026, 6, 15);
    private static readonly Guid PartnerA = UuidV7.NewGuid();
    private static readonly Guid PartnerB = UuidV7.NewGuid();

    private readonly IArInvoiceRepository _arInvoices = Substitute.For<IArInvoiceRepository>();
    private readonly IApInvoiceRepository _apInvoices = Substitute.For<IApInvoiceRepository>();

    /// <summary>A posted, unpaid AR invoice for <paramref name="amount"/> falling due on a date.</summary>
    private static ArInvoice OpenInvoice(Guid partnerId, DateOnly dueDate, decimal amount)
    {
        ArInvoice invoice = ArInvoice.Draft(
            TenantId, StoreId, PartnerId.From(partnerId), $"INV-{dueDate:yyyyMMdd}",
            dueDate.AddDays(-30), dueDate, "ZAR");
        invoice.AddLine("Goods", Rand(amount), string.Empty, Rand(0m));
        invoice.Post(UuidV7.NewGuid());
        return invoice;
    }

    private async Task<IReadOnlyList<AgeingRow>> ArAgeing(params ArInvoice[] invoices)
    {
        _arInvoices.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(invoices);
        return await new GetArAgeingQueryHandler(_arInvoices).HandleAsync(new GetArAgeingQuery(AsOf));
    }

    [Fact]
    public async Task An_invoice_not_yet_due_falls_in_current()
    {
        IReadOnlyList<AgeingRow> ageing = await ArAgeing(OpenInvoice(PartnerA, AsOf.AddDays(10), 100m));

        AgeingRow row = ageing.Should().ContainSingle().Subject;
        row.Current.Should().Be(100m);
        row.Days30.Should().Be(0m);
        row.Total.Should().Be(100m);
    }

    [Fact]
    public async Task An_invoice_due_exactly_today_is_current_not_overdue()
    {
        // The boundary that decides whether a customer gets a dunning letter on the due date itself.
        IReadOnlyList<AgeingRow> ageing = await ArAgeing(OpenInvoice(PartnerA, AsOf, 100m));

        ageing.Single().Current.Should().Be(100m);
        ageing.Single().Days30.Should().Be(0m);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public async Task An_invoice_between_one_and_thirty_days_overdue_falls_in_the_thirty_day_bucket(int daysOverdue)
    {
        IReadOnlyList<AgeingRow> ageing = await ArAgeing(OpenInvoice(PartnerA, AsOf.AddDays(-daysOverdue), 100m));

        ageing.Single().Days30.Should().Be(100m);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(60)]
    public async Task An_invoice_between_thirty_one_and_sixty_days_overdue_falls_in_the_sixty_day_bucket(int daysOverdue)
    {
        IReadOnlyList<AgeingRow> ageing = await ArAgeing(OpenInvoice(PartnerA, AsOf.AddDays(-daysOverdue), 100m));

        ageing.Single().Days60.Should().Be(100m);
    }

    [Theory]
    [InlineData(61)]
    [InlineData(90)]
    public async Task An_invoice_between_sixty_one_and_ninety_days_overdue_falls_in_the_ninety_day_bucket(int daysOverdue)
    {
        IReadOnlyList<AgeingRow> ageing = await ArAgeing(OpenInvoice(PartnerA, AsOf.AddDays(-daysOverdue), 100m));

        ageing.Single().Days90.Should().Be(100m);
    }

    [Fact]
    public async Task An_invoice_more_than_ninety_days_overdue_falls_in_the_ninety_plus_bucket()
    {
        IReadOnlyList<AgeingRow> ageing = await ArAgeing(OpenInvoice(PartnerA, AsOf.AddDays(-91), 100m));

        ageing.Single().Days90Plus.Should().Be(100m);
    }

    [Fact]
    public async Task A_partners_invoices_are_bucketed_together_and_the_total_is_the_sum_of_the_buckets()
    {
        IReadOnlyList<AgeingRow> ageing = await ArAgeing(
            OpenInvoice(PartnerA, AsOf.AddDays(10), 100m),
            OpenInvoice(PartnerA, AsOf.AddDays(-5), 50m),
            OpenInvoice(PartnerA, AsOf.AddDays(-45), 25m),
            OpenInvoice(PartnerA, AsOf.AddDays(-100), 10m));

        AgeingRow row = ageing.Should().ContainSingle().Subject;
        row.Current.Should().Be(100m);
        row.Days30.Should().Be(50m);
        row.Days60.Should().Be(25m);
        row.Days90.Should().Be(0m);
        row.Days90Plus.Should().Be(10m);
        row.Total.Should().Be(185m);
    }

    [Fact]
    public async Task Each_partner_gets_its_own_row()
    {
        IReadOnlyList<AgeingRow> ageing = await ArAgeing(
            OpenInvoice(PartnerA, AsOf.AddDays(-5), 50m),
            OpenInvoice(PartnerB, AsOf.AddDays(-5), 75m));

        ageing.Should().HaveCount(2);
        ageing.Single(row => row.PartnerId == PartnerA).Total.Should().Be(50m);
        ageing.Single(row => row.PartnerId == PartnerB).Total.Should().Be(75m);
    }

    [Fact]
    public async Task An_invoice_ages_by_what_is_still_outstanding_not_by_what_it_was_invoiced_for()
    {
        ArInvoice partlyPaid = OpenInvoice(PartnerA, AsOf.AddDays(-5), 100m);
        partlyPaid.Allocate(Rand(60m));

        IReadOnlyList<AgeingRow> ageing = await ArAgeing(partlyPaid);

        ageing.Single().Days30.Should().Be(40m);
    }

    [Fact]
    public async Task Nothing_open_ages_to_nothing()
    {
        IReadOnlyList<AgeingRow> ageing = await ArAgeing();

        ageing.Should().BeEmpty();
    }

    [Fact]
    public async Task Ap_ageing_buckets_supplier_invoices_the_same_way()
    {
        ApInvoice invoice = ApInvoice.Draft(
            TenantId, StoreId, PartnerId.From(PartnerA), "SUPP-INV-77",
            AsOf.AddDays(-75), AsOf.AddDays(-45), "ZAR");
        invoice.AddLine("Stock", Rand(200m), string.Empty, Rand(0m));
        invoice.Post(UuidV7.NewGuid());
        _apInvoices.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([invoice]);

        IReadOnlyList<AgeingRow> ageing =
            await new GetApAgeingQueryHandler(_apInvoices).HandleAsync(new GetApAgeingQuery(AsOf));

        ageing.Should().ContainSingle().Which.Days60.Should().Be(200m);
    }
}
