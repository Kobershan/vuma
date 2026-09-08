using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.CustomerAccounts;

/// <summary>
/// The lay-by lifecycle: frozen price, accumulating payments, exactly-once completion, and
/// cancellation that accounts for every cent.
/// </summary>
public sealed class LayByLifecycleTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid PartnerId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Deposit_then_three_instalments_pays_R1200_exactly()
    {
        LayByAgreement agreement = ActiveAgreement(total: 1200m);

        agreement.AddInstalment(Row(2, 200m));
        agreement.AddInstalment(Row(3, 200m));
        agreement.AddInstalment(Row(4, 200m));
        agreement.AddInstalment(Row(5, 200m));
        agreement.AddInstalment(Row(6, 200m));

        agreement.PaidToDate.Amount.Should().Be(1200m);
        agreement.Remaining.Amount.Should().Be(0m);
        agreement.Status.Should().Be(LayByStatus.Active);
    }

    [Fact]
    public void Completion_needs_every_cent_and_stamps_the_instant()
    {
        LayByAgreement agreement = ActiveAgreement(total: 1200m);

        Action early = () => agreement.Complete(Now);
        early.Should().Throw<LayByExceptions>();

        agreement.AddInstalment(Row(2, 200m));
        agreement.AddInstalment(Row(3, 200m));
        agreement.AddInstalment(Row(4, 200m));
        agreement.AddInstalment(Row(5, 200m));
        agreement.AddInstalment(Row(6, 200m));

        agreement.Complete(Now);

        agreement.Status.Should().Be(LayByStatus.Completed);
        agreement.CompletedAt.Should().Be(Now);

        Action again = () => agreement.Complete(Now);
        again.Should().Throw<LayByExceptions>();
    }

    [Fact]
    public void Overpayment_is_refused_because_money_without_a_home_is_a_dispute()
    {
        LayByAgreement agreement = ActiveAgreement(total: 1200m);

        Action over = () => agreement.AddInstalment(Row(2, 1100m));

        over.Should().Throw<LayByExceptions>();
        agreement.PaidToDate.Amount.Should().Be(200m);
    }

    [Fact]
    public void Cancellation_accounts_for_every_cent_with_the_fee_capped()
    {
        // Paid R800 (deposit R200 + 3 × R200): refund R700, fee R100.
        LayByAgreement agreement = ActiveAgreement(total: 1200m);
        agreement.AddInstalment(Row(2, 200m));
        agreement.AddInstalment(Row(3, 200m));
        agreement.AddInstalment(Row(4, 200m));

        agreement.Cancel(Now, new Money(700m, "ZAR"), new Money(100m, "ZAR"));

        agreement.Status.Should().Be(LayByStatus.Cancelled);
        agreement.CancelRefund.Should().Be(new Money(700m, "ZAR"));
        agreement.CancelFee.Should().Be(new Money(100m, "ZAR"));

        // A fee above the snapshotted R100 terms is refused even when the arithmetic balances.
        LayByAgreement rich = ActiveAgreement(total: 1200m);
        rich.AddInstalment(Row(2, 200m));

        Action greedy = () => rich.Cancel(Now, new Money(200m, "ZAR"), new Money(200m, "ZAR"));
        greedy.Should().Throw<LayByExceptions>();

        // And refund-plus-fee must equal paid: R399 + R100 against R400 paid refuses.
        LayByAgreement shortPaid = ActiveAgreement(total: 1200m);
        shortPaid.AddInstalment(Row(2, 200m));

        Action shortCount = () => shortPaid.Cancel(Now, new Money(299m, "ZAR"), new Money(100m, "ZAR"));
        shortCount.Should().Throw<LayByExceptions>();
    }

    [Fact]
    public void Frozen_lines_hold_their_price_whatever_the_shelf_does()
    {
        LayByAgreement agreement = ActiveAgreement(total: 100m, firstPayment: 100m);

        agreement.Lines.Should().ContainSingle();
        agreement.Lines[0].AgreedUnitPrice.Amount.Should().Be(100m);
        agreement.Lines[0].Net.Amount.Should().BeApproximately(86.9565m, 0.0001m);
        agreement.Lines[0].TaxAmount.Amount.Should().BeApproximately(13.0435m, 0.0001m);
        agreement.AgreedTotal.Amount.Should().Be(100m);
    }

    [Fact]
    public void Instalments_only_land_on_an_active_agreement()
    {
        LayByAgreement draft = DraftAgreement(total: 1200m);

        Action paying = () => draft.AddInstalment(Row(1, 200m));

        paying.Should().Throw<LayByExceptions>();
    }

    private static LayByAgreement ActiveAgreement(decimal total, decimal firstPayment = 200m)
    {
        LayByAgreement agreement = DraftAgreement(total, firstPayment);
        agreement.AddLine(Line(total));
        agreement.Activate();
        agreement.AddInstalment(Row(1, firstPayment));
        return agreement;
    }

    private static LayByAgreement DraftAgreement(decimal total, decimal deposit = 200m)
    {
        return LayByAgreement.Open(
            TenantId, StoreId, $"LAY-{UuidV7.NewGuid():N}", PartnerId,
            new Money(total, "ZAR"), new Money(deposit, "ZAR"), 3,
            Now.AddMonths(3), new Money(100m, "ZAR"), UuidV7.NewGuid());
    }

    private static LayByAgreementLine Line(decimal total)
    {
        decimal net = total - total * 15m / 115m;
        decimal tax = total * 15m / 115m;
        return LayByAgreementLine.Create(
            TenantId, StoreId, UuidV7.NewGuid(), ItemId, null,
            1m, "EA", new Money(100m, "ZAR"), new Money(100m - net, "ZAR"),
            new Money(tax, "ZAR"), "Each", "ZAR", null);
    }

    private static LayByInstalment Row(int sequence, decimal amount)
    {
        return LayByInstalment.Record(
            TenantId, StoreId, UuidV7.NewGuid(), sequence,
            new Money(amount, "ZAR"), $"RCPT-{sequence}", Now, "Till");
    }
}
