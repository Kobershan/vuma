using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Registry;

public sealed class RegistrySagaTests
{
    [Fact]
    public void Intent_is_authorized_then_completes_only_after_start()
    {
        var intent = SagaIntent.Create(UuidV7.NewGuid(), "split-sale", "request-1", DateTimeOffset.UtcNow, "{}");
        intent.Authorize(UuidV7.NewGuid(), "user:operator", new HlcStamp(1, 0, "store:test").ToString());
        intent.AddLeg(UuidV7.NewGuid());

        var complete = () => intent.Complete();
        complete.Should().Throw<InvalidOperationException>();
        intent.Start("worker:registry");
        var partial = () => intent.Complete();
        partial.Should().Throw<InvalidOperationException>();
        intent.Legs[0].MarkDispatched();
        intent.Legs[0].Acknowledge(DateTimeOffset.UtcNow);
        intent.Complete();
        intent.State.Should().Be(SagaIntentState.Completed);
    }

    [Fact]
    public void Acknowledged_leg_cannot_be_replayed_or_failed()
    {
        var leg = new SagaLeg(UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid());
        leg.MarkDispatched();
        leg.Acknowledge(DateTimeOffset.UtcNow);

        var dispatch = () => leg.MarkDispatched();
        var fail = () => leg.Fail("late failure");
        dispatch.Should().Throw<InvalidOperationException>();
        fail.Should().Throw<InvalidOperationException>();
        leg.Attempts.Should().Be(1);
    }

    [Fact]
    public void Group_rejects_duplicate_members()
    {
        var group = CompanyGroup.Create(UuidV7.NewGuid(), "Trading group");
        var company = UuidV7.NewGuid();
        group.AddMember(company);

        var duplicate = () => group.AddMember(company);
        duplicate.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Intent_rejects_duplicate_leg_ids_and_outbox_requires_idempotency()
    {
        var intent = SagaIntent.Create(UuidV7.NewGuid(), "split-sale", "request-2", DateTimeOffset.UtcNow);
        var legId = UuidV7.NewGuid();
        intent.AddLeg(UuidV7.NewGuid(), legId);

        var duplicate = () => intent.AddLeg(UuidV7.NewGuid(), legId);
        duplicate.Should().Throw<InvalidOperationException>();

        var outbox = new RegistryOutboxMessage(intent.TenantId, "saga.dispatch", "{}", DateTimeOffset.UtcNow, " dispatch-1 ");
        outbox.IdempotencyKey.Should().Be("dispatch-1");
    }
}
