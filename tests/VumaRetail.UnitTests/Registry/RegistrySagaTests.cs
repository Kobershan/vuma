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
        var leg = NewLeg();
        leg.MarkDispatched();
        leg.Acknowledge(DateTimeOffset.UtcNow);

        var dispatch = () => leg.MarkDispatched();
        var fail = () => leg.Fail("late failure");
        dispatch.Should().Throw<InvalidOperationException>();
        fail.Should().Throw<InvalidOperationException>();
        leg.Attempts.Should().Be(1);
    }

    [Fact]
    public void Repeated_acknowledgement_is_idempotent_and_preserves_the_first_completion_time()
    {
        var leg = NewLeg();
        var acknowledgedAt = DateTimeOffset.UtcNow;
        leg.MarkDispatched(acknowledgedAt);
        leg.Acknowledge(acknowledgedAt);

        leg.Acknowledge(acknowledgedAt.AddMinutes(1));

        leg.State.Should().Be(SagaLegState.Acknowledged);
        leg.AcknowledgedAt.Should().Be(acknowledgedAt);
        leg.Attempts.Should().Be(1);
    }

    [Fact]
    public void Timed_out_intent_can_be_compensated_after_partial_leg_completion()
    {
        var intent = SagaIntent.Create(UuidV7.NewGuid(), "group-receipt", "request-3", DateTimeOffset.UtcNow);
        intent.Authorize(UuidV7.NewGuid(), "user:operator", new HlcStamp(1, 0, "store:test").ToString());
        intent.AddLeg(UuidV7.NewGuid());
        intent.AddLeg(UuidV7.NewGuid());
        intent.Start("worker:registry");
        intent.Legs[0].MarkDispatched();
        intent.Legs[0].Acknowledge(DateTimeOffset.UtcNow);
        intent.Legs[1].MarkDispatched();
        intent.Timeout(DateTimeOffset.UtcNow.AddMinutes(15));

        intent.Compensate();

        intent.State.Should().Be(SagaIntentState.Compensated);
        intent.Legs[0].State.Should().Be(SagaLegState.Acknowledged);
        intent.Legs[1].State.Should().Be(SagaLegState.Compensated);
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

    [Fact]
    public void Registry_legs_require_tenant_scoping_at_construction()
    {
        var create = () => new SagaLeg(UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid(), Guid.Empty);

        create.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("tenantId");
    }

    [Fact]
    public void Intent_and_outbox_payloads_redact_secrets_and_require_json()
    {
        var tenantId = UuidV7.NewGuid();
        var intent = SagaIntent.Create(tenantId, "split-sale", "request-4", DateTimeOffset.UtcNow,
            "{\"order\":{\"total\":42},\"password\":\"do-not-store\",\"nested\":[{\"accessToken\":\"also-secret\"}]}" );
        var outbox = new RegistryOutboxMessage(tenantId, "saga.dispatch",
            "{\"connectionString\":\"Host=private\",\"value\":true}", DateTimeOffset.UtcNow);

        intent.Payload.Should().NotContain("do-not-store").And.Contain("[REDACTED]");
        intent.Payload.Should().NotContain("also-secret");
        outbox.Payload.Should().NotContain("Host=private").And.Contain("[REDACTED]");

        var invalid = () => SagaIntent.Create(tenantId, "split-sale", "request-5", DateTimeOffset.UtcNow, "not-json");
        invalid.Should().Throw<ArgumentException>();
    }

    private static SagaLeg NewLeg()
        => new(UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid());
}
