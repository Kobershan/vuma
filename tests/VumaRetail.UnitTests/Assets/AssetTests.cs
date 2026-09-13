using FluentAssertions;
using VumaRetail.Domain.Assets;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Assets;

public sealed class AssetTests
{
    [Fact]
    public void Depreciation_stops_at_residual_after_useful_life()
    {
        FixedAsset asset = FixedAsset.Create(Guid.NewGuid(), null, Guid.NewGuid(), "A-1", "Till",
            new DateOnly(2026, 1, 1), new Money(12000m, "ZAR"));
        AssetBook book = AssetBook.Create(asset.TenantId, null, asset.CompanyId!.Value, asset.Id, "Local",
            new DateOnly(2026, 1, 1), Money.Zero("ZAR"), 12);
        DepreciationCharge charge = DepreciationCalculator.Calculate(asset, book, new DateOnly(2027, 1, 1));
        charge.Amount.Amount.Should().Be(0m);
        charge.ClosingNetBookValue.Amount.Should().Be(0m);
    }

    [Fact]
    public void Asset_lifecycle_requires_draft_then_in_service_before_disposal()
    {
        FixedAsset asset = FixedAsset.Create(Guid.NewGuid(), null, Guid.NewGuid(), "A-2", "Till",
            new DateOnly(2026, 1, 1), new Money(100m, "ZAR"));

        FluentActions.Invoking(() => asset.Dispose(new DateOnly(2026, 2, 1)))
            .Should().Throw<InvalidOperationException>();
        asset.PlaceInService();
        asset.Dispose(new DateOnly(2026, 2, 1));
        asset.Status.Should().Be(AssetStatus.Disposed);
    }

    [Fact]
    public void Checklist_execution_preserves_device_and_capture_time_for_offline_replay()
    {
        var captured = new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeSpan.Zero);
        var checklist = StoreChecklist.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "open", "Opening",
            ["cash", "safe"]);
        var execution = ChecklistExecution.Submit(checklist.TenantId, checklist.StoreId, checklist.CompanyId!.Value,
            checklist.Id, Guid.NewGuid(), "device-7", captured, captured.AddMinutes(4), "blob/check-1.json");

        checklist.Code.Should().Be("OPEN");
        checklist.ItemCodes.Should().Be("CASH|SAFE");
        execution.DeviceId.Should().Be("device-7");
        execution.CapturedAt.Should().Be(captured);
    }

    [Fact]
    public void Checklist_execution_rejects_submission_before_capture()
    {
        var action = () => ChecklistExecution.Submit(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "device-7", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(-1), "evidence");
        action.Should().Throw<ArgumentException>();
    }
}
