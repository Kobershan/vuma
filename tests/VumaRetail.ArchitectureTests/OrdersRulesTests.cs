using NetArchTest.Rules;

namespace VumaRetail.ArchitectureTests;

/// <summary>
/// Stage 14's own architectural claim: it is a caller of Stage 13's warehouse allocation, never the
/// other way round.
/// </summary>
/// <remarks>
/// <c>VumaRetail.Application.Warehouse</c> and <c>VumaRetail.Application.Orders</c> live in the same
/// assembly, so a wrong-direction reference between them is not a project-reference compile error the
/// way <c>LayeringTests</c>' Domain/Application/Infrastructure checks are — it needs its own assertion.
/// The rest of this stage's architectural obligations (no <c>orders</c> type naming a GL account,
/// every new command swept by <c>CommandClassificationTests</c>, every new replicated entity seen by
/// <c>ReplicationRulesTests</c>, no cross-schema foreign key) are already covered by those existing
/// generic sweeps without any change to them — this file exists for the one claim that is not.
/// </remarks>
public sealed class OrdersRulesTests
{
    private const string WarehouseNamespace = "VumaRetail.Application.Warehouse";
    private const string OrdersNamespace = "VumaRetail.Application.Orders";

    [Fact]
    public void Warehouse_does_not_depend_on_orders()
    {
        Types.InAssembly(typeof(VumaRetail.Application.AssemblyMarker).Assembly)
            .That()
            .ResideInNamespaceStartingWith(WarehouseNamespace)
            .Should()
            .NotHaveDependencyOn(OrdersNamespace)
            .GetResult()
            .ShouldPass(
                "Stage 13 (warehouse) must never depend on Stage 14 (orders) — orders is the caller of "
                + "the unchanged PickTask/PickWave seam, not a peer warehouse grew a reference to.");
    }
}
