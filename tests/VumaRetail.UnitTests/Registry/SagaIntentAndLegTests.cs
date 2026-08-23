using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Registry;

public sealed class SagaIntentAndLegTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Raised = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Settled = Raised.AddMinutes(2);

    [Fact]
    public void An_intent_is_raised_with_the_full_description_and_no_start_or_settle_time_yet()
    {
        SagaIntent intent = SagaIntent.Raise(TenantId, "GroupReceiptAllocation", """{"total":9000}""");

        intent.Status.Should().Be(SagaIntentStatus.Raised);
        intent.Kind.Should().Be("GroupReceiptAllocation");
        intent.Payload.Should().Be("""{"total":9000}""");
        intent.StartedAt.Should().BeNull();
        intent.SettledAt.Should().BeNull();
    }

    [Fact]
    public void Moving_off_Raised_stamps_StartedAt_exactly_once()
    {
        SagaIntent intent = SagaIntent.Raise(TenantId, "GroupReceiptAllocation", "{}");

        intent.MoveTo(SagaIntentStatus.InProgress, Raised);
        intent.StartedAt.Should().Be(Raised);

        // A later transition must not re-stamp the start — that would make an intent that stalled and
        // resumed look like it only just began.
        intent.MoveTo(SagaIntentStatus.InProgress, Raised.AddMinutes(1));
        intent.StartedAt.Should().Be(Raised);
    }

    [Fact]
    public void Reaching_Settled_stamps_SettledAt()
    {
        SagaIntent intent = SagaIntent.Raise(TenantId, "GroupReceiptAllocation", "{}");
        intent.MoveTo(SagaIntentStatus.InProgress, Raised);

        intent.MoveTo(SagaIntentStatus.Settled, Settled);

        intent.Status.Should().Be(SagaIntentStatus.Settled);
        intent.SettledAt.Should().Be(Settled);
    }

    [Fact]
    public void Reaching_Compensated_also_stamps_SettledAt()
    {
        SagaIntent intent = SagaIntent.Raise(TenantId, "GroupReceiptAllocation", "{}");
        intent.MoveTo(SagaIntentStatus.Compensating, Raised);

        intent.MoveTo(SagaIntentStatus.Compensated, Settled);

        intent.SettledAt.Should().Be(Settled);
    }

    [Fact]
    public void A_leg_is_keyed_by_its_intent_and_a_stable_leg_key()
    {
        Guid intentId = UuidV7.NewGuid();
        Guid companyId = UuidV7.NewGuid();

        SagaLeg leg = SagaLeg.Create(TenantId, intentId, "hardware-allocation", companyId);

        leg.IntentId.Should().Be(intentId);
        leg.LegKey.Should().Be("hardware-allocation");
        leg.CompanyId.Should().Be(companyId);
        leg.Status.Should().Be(SagaLegStatus.Pending);
        leg.AttemptCount.Should().Be(0);
    }

    [Fact]
    public void Dispatch_increments_the_attempt_count_every_time_including_retries()
    {
        SagaLeg leg = SagaLeg.Create(TenantId, UuidV7.NewGuid(), "leg-1", UuidV7.NewGuid());

        leg.MarkDispatched();
        leg.MarkFailed("connection refused");
        leg.MarkDispatched();

        leg.AttemptCount.Should().Be(2);
        leg.Status.Should().Be(SagaLegStatus.Dispatched);
    }

    [Fact]
    public void Acknowledging_a_leg_clears_any_prior_error()
    {
        SagaLeg leg = SagaLeg.Create(TenantId, UuidV7.NewGuid(), "leg-1", UuidV7.NewGuid());
        leg.MarkDispatched();
        leg.MarkFailed("timeout");

        leg.MarkAcknowledged(Settled);

        leg.Status.Should().Be(SagaLegStatus.Acknowledged);
        leg.AcknowledgedAt.Should().Be(Settled);
        leg.LastError.Should().BeNull();
    }

    [Fact]
    public void A_long_error_is_truncated_to_the_stored_column_width()
    {
        SagaLeg leg = SagaLeg.Create(TenantId, UuidV7.NewGuid(), "leg-1", UuidV7.NewGuid());

        leg.MarkFailed(new string('x', SagaLeg.MaxErrorLength + 500));

        leg.LastError.Should().HaveLength(SagaLeg.MaxErrorLength);
    }

    [Fact]
    public void A_leg_must_target_a_company()
    {
        Action create = () => SagaLeg.Create(TenantId, UuidV7.NewGuid(), "leg-1", Guid.Empty);

        create.Should().Throw<ArgumentException>();
    }
}
