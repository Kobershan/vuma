using NSubstitute;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Registry;

namespace VumaRetail.UnitTests.Registry;

/// <summary>
/// A period cannot close while inter-company work involving the closing company is still
/// outstanding — and the refusal names the blocking intents (Stage 07c, MULTI_COMPANY.md §7).
/// </summary>
public sealed class PeriodCloseGuardTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid CompanyB = Guid.NewGuid();
    private static readonly Guid CompanyC = Guid.NewGuid();

    private readonly IGroupReceiptRepository _repository = Substitute.For<IGroupReceiptRepository>();

    private PeriodCloseGuard Guard => new(_repository);

    private static GroupReceipt Captured()
        => GroupReceipt.Capture(
            TenantId, CompanyA, Guid.NewGuid(), new Money(9000m, "ZAR"),
            "EFT", "MKHIZE 22/08", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Check_passes_when_nothing_is_outstanding()
    {
        _repository.GetOutstandingIntentsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns([]);
        _repository.GetUnallocatedAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns([]);

        await Guard.CheckAsync(TenantId, CompanyA);

        // No throw: the close may proceed.
    }

    [Fact]
    public async Task Check_refuses_and_names_the_clearing_intent()
    {
        var intent = InterCompanyClearingIntent.Create(
            TenantId, Guid.NewGuid(), "group-receipt",
            CompanyA, CompanyB, new Money(3000m, "ZAR"), "ZAR");
        _repository.GetOutstandingIntentsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns([intent]);
        _repository.GetUnallocatedAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns([]);

        var refusal = await Assert.ThrowsAsync<PeriodCloseBlockedByIntentsException>(
            () => Guard.CheckAsync(TenantId, CompanyA));

        Assert.Contains(intent.Id, refusal.IntentIds);
    }

    [Fact]
    public async Task Check_refuses_when_a_sister_company_leg_is_pending()
    {
        var intent = InterCompanyClearingIntent.Create(
            TenantId, Guid.NewGuid(), "group-receipt",
            CompanyA, CompanyB, new Money(3000m, "ZAR"), "ZAR");
        _repository.GetOutstandingIntentsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns([intent]);
        _repository.GetUnallocatedAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns([]);

        // The bank owner's close is blocked by its own clearing leg too, not just the sister's.
        var refusal = await Assert.ThrowsAsync<PeriodCloseBlockedByIntentsException>(
            () => Guard.CheckAsync(TenantId, CompanyB));

        Assert.Contains(intent.Id, refusal.IntentIds);
    }

    [Fact]
    public async Task Check_ignores_intents_that_do_not_touch_the_closing_company()
    {
        var intent = InterCompanyClearingIntent.Create(
            TenantId, Guid.NewGuid(), "group-receipt",
            CompanyA, CompanyB, new Money(3000m, "ZAR"), "ZAR");
        _repository.GetOutstandingIntentsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns([intent]);
        _repository.GetUnallocatedAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns([]);

        await Guard.CheckAsync(TenantId, CompanyC);
    }

    [Fact]
    public async Task Check_refuses_when_a_receipt_allocation_is_pending()
    {
        GroupReceipt receipt = Captured();
        receipt.Allocate(CompanyB, null, new Money(3000m, "ZAR"));
        _repository.GetOutstandingIntentsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns([]);
        _repository.GetUnallocatedAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns([receipt]);

        var refusal = await Assert.ThrowsAsync<PeriodCloseBlockedByIntentsException>(
            () => Guard.CheckAsync(TenantId, CompanyB));

        Assert.Contains(receipt.Id, refusal.IntentIds);
    }

    [Fact]
    public async Task Check_passes_when_every_allocation_applied()
    {
        GroupReceipt receipt = Captured();
        var allocation = receipt.Allocate(CompanyB, null, new Money(9000m, "ZAR"));
        receipt.AcknowledgeAllocation(allocation.Id);
        _repository.GetOutstandingIntentsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns([]);
        _repository.GetUnallocatedAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns([]);

        await Guard.CheckAsync(TenantId, CompanyB);
    }
}
