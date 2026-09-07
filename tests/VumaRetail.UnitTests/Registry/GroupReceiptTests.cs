using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using Xunit;

namespace VumaRetail.UnitTests.Registry;

public class GroupReceiptTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid CompanyB = Guid.NewGuid();
    private static readonly Guid CompanyC = Guid.NewGuid();
    private static readonly Guid BankAccountId = Guid.NewGuid();
    private static readonly Money NineThousand = new(9000m, "ZAR");

    [Fact]
    public void Capture_creates_receipt_with_status_Draft()
    {
        GroupReceipt receipt = GroupReceipt.Capture(
            TenantId, CompanyA, BankAccountId, NineThousand, "EFT", "MKHIZE 22/08", DateTimeOffset.UtcNow);

        Assert.Equal(GroupReceiptStatus.Draft, receipt.Status);
        Assert.Equal(NineThousand, receipt.Amount);
        Assert.Empty(receipt.Allocations);
    }

    [Fact]
    public void Allocate_adds_allocation_and_transitions_to_PartiallyAllocated()
    {
        GroupReceipt receipt = GroupReceipt.Capture(
            TenantId, CompanyA, BankAccountId, NineThousand, "EFT", "REF", DateTimeOffset.UtcNow);

        GroupReceiptAllocation allocation = receipt.Allocate(CompanyB, null, new Money(3000m, "ZAR"));

        Assert.Equal(GroupReceiptStatus.PartiallyAllocated, receipt.Status);
        Assert.Single(receipt.Allocations);
        Assert.Equal(new Money(3000m, "ZAR"), allocation.Amount);
        Assert.Equal(GroupReceiptAllocationLegState.Pending, allocation.LegState);
    }

    [Fact]
    public void Allocate_full_amount_transitions_to_Allocated()
    {
        GroupReceipt receipt = GroupReceipt.Capture(
            TenantId, CompanyA, BankAccountId, NineThousand, "EFT", "REF", DateTimeOffset.UtcNow);

        receipt.Allocate(CompanyB, null, new Money(1000m, "ZAR"));
        receipt.Allocate(CompanyC, null, new Money(3000m, "ZAR"));
        receipt.Allocate(CompanyA, null, new Money(5000m, "ZAR"));

        Assert.Equal(GroupReceiptStatus.Allocated, receipt.Status);
    }

    [Fact]
    public void Allocate_throws_when_exceeding_amount()
    {
        GroupReceipt receipt = GroupReceipt.Capture(
            TenantId, CompanyA, BankAccountId, NineThousand, "EFT", "REF", DateTimeOffset.UtcNow);

        receipt.Allocate(CompanyB, null, new Money(6000m, "ZAR"));

        Assert.Throws<InvalidOperationException>(() =>
            receipt.Allocate(CompanyC, null, new Money(4000m, "ZAR")));
    }

    [Fact]
    public void Allocate_throws_on_currency_mismatch()
    {
        GroupReceipt receipt = GroupReceipt.Capture(
            TenantId, CompanyA, BankAccountId, NineThousand, "EFT", "REF", DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() =>
            receipt.Allocate(CompanyB, null, new Money(1000m, "USD")));
    }

    [Fact]
    public void Allocate_zero_throws()
    {
        GroupReceipt receipt = GroupReceipt.Capture(
            TenantId, CompanyA, BankAccountId, NineThousand, "EFT", "REF", DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() =>
            receipt.Allocate(CompanyB, null, new Money(0m, "ZAR")));
    }

    [Fact]
    public void Allocate_negative_throws()
    {
        GroupReceipt receipt = GroupReceipt.Capture(
            TenantId, CompanyA, BankAccountId, NineThousand, "EFT", "REF", DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() =>
            receipt.Allocate(CompanyB, null, new Money(-100m, "ZAR")));
    }

    [Fact]
    public void Allocate_throws_when_fully_allocated()
    {
        GroupReceipt receipt = GroupReceipt.Capture(
            TenantId, CompanyA, BankAccountId, NineThousand, "EFT", "REF", DateTimeOffset.UtcNow);

        receipt.Allocate(CompanyB, null, new Money(9000m, "ZAR"));

        Assert.Throws<InvalidOperationException>(() =>
            receipt.Allocate(CompanyC, null, new Money(100m, "ZAR")));
    }

    [Fact]
    public void Reverse_dispatches_compensating_legs()
    {
        GroupReceipt receipt = GroupReceipt.Capture(
            TenantId, CompanyA, BankAccountId, NineThousand, "EFT", "REF", DateTimeOffset.UtcNow);

        receipt.Allocate(CompanyB, null, new Money(3000m, "ZAR"));
        receipt.Allocate(CompanyC, null, new Money(5000m, "ZAR"));

        receipt.Reverse();

        Assert.Equal(GroupReceiptStatus.Reversed, receipt.Status);
        Assert.All(receipt.Allocations, a =>
            Assert.Equal(GroupReceiptAllocationLegState.Compensated, a.LegState));
    }

    [Fact]
    public void Reverse_already_reversed_throws()
    {
        GroupReceipt receipt = GroupReceipt.Capture(
            TenantId, CompanyA, BankAccountId, NineThousand, "EFT", "REF", DateTimeOffset.UtcNow);

        receipt.Reverse();

        Assert.Throws<InvalidOperationException>(() => receipt.Reverse());
    }

    [Fact]
    public void AcknowledgeAllocation_marks_applied()
    {
        GroupReceipt receipt = GroupReceipt.Capture(
            TenantId, CompanyA, BankAccountId, NineThousand, "EFT", "REF", DateTimeOffset.UtcNow);

        GroupReceiptAllocation allocation = receipt.Allocate(CompanyB, null, new Money(3000m, "ZAR"));

        receipt.AcknowledgeAllocation(allocation.Id);

        Assert.Equal(GroupReceiptAllocationLegState.Applied, allocation.LegState);
        Assert.NotNull(allocation.AppliedAt);
    }

    [Fact]
    public void Allocations_do_not_over_allocate_across_companies()
    {
        GroupReceipt receipt = GroupReceipt.Capture(
            TenantId, CompanyA, BankAccountId, NineThousand, "EFT", "REF", DateTimeOffset.UtcNow);

        receipt.Allocate(CompanyB, null, new Money(4000m, "ZAR"));
        receipt.Allocate(CompanyC, null, new Money(4000m, "ZAR"));

        // Third allocation should fail because only 1000 left
        Assert.Throws<InvalidOperationException>(() =>
            receipt.Allocate(CompanyA, null, new Money(2000m, "ZAR")));
    }

    [Fact]
    public void Operator_example_R9000_three_companies()
    {
        GroupReceipt receipt = GroupReceipt.Capture(
            TenantId, CompanyA, BankAccountId, NineThousand, "EFT", "MKHIZE 22/08", DateTimeOffset.UtcNow);

        GroupReceiptAllocation alloc1 = receipt.Allocate(CompanyA, null, new Money(1000m, "ZAR"));
        GroupReceiptAllocation alloc2 = receipt.Allocate(CompanyB, null, new Money(3000m, "ZAR"));
        GroupReceiptAllocation alloc3 = receipt.Allocate(CompanyC, null, new Money(5000m, "ZAR"));

        Assert.Equal(GroupReceiptStatus.Allocated, receipt.Status);
        Assert.Equal(3, receipt.Allocations.Count);
        Assert.Equal(new Money(1000m, "ZAR"), alloc1.Amount);
        Assert.Equal(new Money(3000m, "ZAR"), alloc2.Amount);
        Assert.Equal(new Money(5000m, "ZAR"), alloc3.Amount);
    }
}

public class GroupPaymentRunTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid CompanyB = Guid.NewGuid();
    private static readonly Guid BankAccountId = Guid.NewGuid();
    private static readonly Money FiveThousand = new(5000m, "ZAR");

    [Fact]
    public void Capture_creates_payment_run()
    {
        GroupPaymentRun payment = GroupPaymentRun.Capture(
            TenantId, CompanyA, BankAccountId, FiveThousand, "EFT", "SUPPLIER-001", DateTimeOffset.UtcNow);

        Assert.Equal(GroupPaymentRunStatus.Draft, payment.Status);
        Assert.Equal(FiveThousand, payment.Amount);
    }

    [Fact]
    public void Allocate_adds_allocation()
    {
        GroupPaymentRun payment = GroupPaymentRun.Capture(
            TenantId, CompanyA, BankAccountId, FiveThousand, "EFT", "SUPPLIER-001", DateTimeOffset.UtcNow);

        payment.Allocate(CompanyB, null, new Money(2000m, "ZAR"));

        Assert.Equal(GroupPaymentRunStatus.PartiallyAllocated, payment.Status);
        Assert.Single(payment.Allocations);
    }

    [Fact]
    public void Reverse_compensates_all_legs()
    {
        GroupPaymentRun payment = GroupPaymentRun.Capture(
            TenantId, CompanyA, BankAccountId, FiveThousand, "EFT", "SUPPLIER-001", DateTimeOffset.UtcNow);

        payment.Allocate(CompanyB, null, new Money(2000m, "ZAR"));
        payment.Reverse();

        Assert.Equal(GroupPaymentRunStatus.Reversed, payment.Status);
        Assert.All(payment.Allocations, a =>
            Assert.Equal(GroupPaymentAllocationLegState.Compensated, a.LegState));
    }
}

public class InterCompanyClearingIntentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid CompanyB = Guid.NewGuid();
    private static readonly Money Amount = new(3000m, "ZAR");

    [Fact]
    public void Create_creates_intent_with_two_legs()
    {
        var intent = InterCompanyClearingIntent.Create(
            TenantId, Guid.NewGuid(), "group-receipt",
            CompanyA, CompanyB, Amount, "ZAR");

        Assert.Equal(InterCompanyClearingIntentState.Pending, intent.State);
        Assert.Equal(2, intent.Legs.Count);
        Assert.Equal(CompanyA, intent.FromCompanyId);
        Assert.Equal(CompanyB, intent.ToCompanyId);
    }

    [Fact]
    public void AcknowledgeLeg_sets_state_to_Settled_when_both_ack()
    {
        var intent = InterCompanyClearingIntent.Create(
            TenantId, Guid.NewGuid(), "group-receipt",
            CompanyA, CompanyB, Amount, "ZAR");

        intent.AcknowledgeLeg(intent.Legs[0].Id);
        Assert.Equal(InterCompanyClearingIntentState.PartiallySettled, intent.State);

        intent.AcknowledgeLeg(intent.Legs[1].Id);
        Assert.Equal(InterCompanyClearingIntentState.Settled, intent.State);
        Assert.NotNull(intent.SettledAt);
    }

    [Fact]
    public void Compensate_marks_outstanding_legs()
    {
        var intent = InterCompanyClearingIntent.Create(
            TenantId, Guid.NewGuid(), "group-receipt",
            CompanyA, CompanyB, Amount, "ZAR");

        intent.Compensate();

        Assert.Equal(InterCompanyClearingIntentState.Compensated, intent.State);
        Assert.All(intent.Legs, l =>
            Assert.Equal(InterCompanyClearingLegState.Compensated, l.State));
    }

    [Fact]
    public void Create_same_company_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            InterCompanyClearingIntent.Create(
                TenantId, Guid.NewGuid(), "group-receipt",
                CompanyA, CompanyA, Amount, "ZAR"));
    }

    [Fact]
    public void Create_negative_amount_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            InterCompanyClearingIntent.Create(
                TenantId, Guid.NewGuid(), "group-receipt",
                CompanyA, CompanyB, new Money(-100m, "ZAR"), "ZAR"));
    }

    [Fact]
    public void Clearing_nets_to_zero_after_settlement()
    {
        var intent = InterCompanyClearingIntent.Create(
            TenantId, Guid.NewGuid(), "group-receipt",
            CompanyA, CompanyB, Amount, "ZAR");

        // Leg 1: CompanyA debits clearing
        InterCompanyClearingLeg leg1 = intent.Legs.First(l => l.CompanyId == CompanyA);
        Assert.Equal("Debit", leg1.Direction);

        // Leg 2: CompanyB credits clearing
        InterCompanyClearingLeg leg2 = intent.Legs.First(l => l.CompanyId == CompanyB);
        Assert.Equal("Credit", leg2.Direction);

        // Net-zero: debit = credit = 3000
        Assert.Equal(leg1.Amount.Amount, leg2.Amount.Amount);
        Assert.Equal(leg1.Amount.Amount, Amount.Amount);
    }
}
