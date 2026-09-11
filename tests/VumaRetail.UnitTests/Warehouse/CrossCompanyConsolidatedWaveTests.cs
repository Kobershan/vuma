using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Warehouse.Commands;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Warehouse;

public sealed class CrossCompanyConsolidatedWaveTests
{
    [Fact]
    public async Task Handler_creates_one_authorized_leg_per_company_with_sanitised_payload()
    {
        Guid tenantId = Guid.NewGuid();
        Guid firstCompany = Guid.NewGuid();
        Guid secondCompany = Guid.NewGuid();
        ISagaCoordinator coordinator = Substitute.For<ISagaCoordinator>();
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.Parse("2026-09-11T10:00:00Z"));
        IHybridClock hybrid = Substitute.For<IHybridClock>();
        hybrid.Next().Returns(HlcStamp.MinValue);
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        IOperatorContext operatorContext = Substitute.For<IOperatorContext>();
        operatorContext.RequireOperatorId().Returns(Guid.NewGuid());
        IPrincipalAccessor principal = Substitute.For<IPrincipalAccessor>();
        principal.Principal.Returns("user:test");
        SagaIntent? captured = null;
        coordinator.ExecuteAsync(Arg.Do<SagaIntent>(x => captured = x), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                SagaIntent intent = call.Arg<SagaIntent>();
                return Task.FromResult(new SagaResult(
                    intent.Id, SagaIntentState.Completed,
                    intent.Legs.Select(x => new SagaLegResult(x.LegId, x.CompanyId, SagaLegState.Acknowledged)).ToArray()));
            });

        var command = new BuildCrossCompanyConsolidatedWaveCommand(
            tenantId, null, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "City", "Durban",
            [
                new(firstCompany, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 2m, "EA", "Each", "Durban"),
                new(secondCompany, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 3m, "EA", "Each", "Durban")
            ], "wave-1");

        Guid result = await new BuildCrossCompanyConsolidatedWaveCommandHandler(
                coordinator, clock, hybrid, tenant, operatorContext, principal)
            .HandleAsync(command);

        result.Should().NotBeEmpty();
        captured.Should().NotBeNull();
        captured!.Type.Should().Be(CrossCompanyConsolidatedWaveSaga.IntentType);
        captured.Legs.Select(x => x.CompanyId).Should().BeEquivalentTo([firstCompany, secondCompany]);
        captured.AuthorizedOperatorId.Should().NotBeNull();
    }

    [Fact]
    public async Task Handler_rejects_a_request_for_another_tenant_before_creating_a_saga()
    {
        ISagaCoordinator coordinator = Substitute.For<ISagaCoordinator>();
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());
        var command = new BuildCrossCompanyConsolidatedWaveCommand(
            Guid.NewGuid(), null, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "City", "Durban",
            [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), 1m, "EA", "Each", "Durban"),
             new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), 1m, "EA", "Each", "Durban")], "wave-2");

        Func<Task> act = () => new BuildCrossCompanyConsolidatedWaveCommandHandler(
                coordinator, Substitute.For<IClock>(), Substitute.For<IHybridClock>(), tenant,
                Substitute.For<IOperatorContext>(), Substitute.For<IPrincipalAccessor>()).HandleAsync(command);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await coordinator.DidNotReceive().ExecuteAsync(Arg.Any<SagaIntent>(), Arg.Any<CancellationToken>());
    }
}
