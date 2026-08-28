using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.ArchitectureTests;

/// <summary>
/// The Stage 04 replication rules — the ones a review cannot catch and a runtime failure catches too
/// late.
/// </summary>
/// <remarks>
/// Stage 01 already asserts that every entity <em>declares</em> how it replicates. These two go
/// further and check that the declaration is coherent, and that the one escape hatch from tenant
/// isolation stays a closed list. Both were proven by deliberate violation before being committed.
/// </remarks>
public sealed class ReplicationRulesTests
{
    private static readonly IModel Model = new DesignTimeDbContextFactory().CreateDbContext([]).Model;

    /// <summary>
    /// The conflict policies that settle a divergence by replacing the local row.
    /// </summary>
    /// <remarks>
    /// <see cref="ConflictPolicy.AppendOnly"/> and <see cref="ConflictPolicy.ManualReview"/> are the
    /// two that never overwrite: the first accumulates, the second escalates and leaves the local row
    /// alone.
    /// </remarks>
    private static readonly ConflictPolicy[] OverwritingPolicies =
    [
        ConflictPolicy.LastWriterWins,
        ConflictPolicy.StoreWins,
        ConflictPolicy.CloudWins,
    ];

    [Fact]
    public void An_immutable_record_never_declares_a_conflict_policy_that_overwrites_it()
    {
        // ADR-012 says a posted financial document cannot be edited, and AuditInterceptor enforces it
        // by throwing on any update to an IImmutableRecord. So an immutable entity that declared
        // LastWriterWins would be an entity the sync receiver is *obliged* to overwrite and the
        // persistence layer is obliged to refuse — which is not a design flaw anybody would spot in
        // review, it is a store whose replication starts throwing the first time two nodes touch one
        // invoice, months after the declaration was written.
        //
        // AppendOnly is the right answer for these: entries accumulate, nothing is overwritten
        // (ADR-005), and two nodes writing independent records merge non-destructively.
        List<string> offenders = Model.GetEntityTypes()
            .Where(entityType => typeof(Entity).IsAssignableFrom(entityType.ClrType))
            .Where(entityType => typeof(IImmutableRecord).IsAssignableFrom(entityType.ClrType))
            .Select(entityType => (
                entityType.ClrType,
                Declaration: entityType.ClrType.GetCustomAttribute<ReplicatedAttribute>()))
            .Where(entry => entry.Declaration is not null)
            .Where(entry => entry.Declaration!.Scope != ReplicationScope.NodeLocal)
            .Where(entry => Array.IndexOf(OverwritingPolicies, entry.Declaration!.ConflictPolicy) >= 0)
            .Select(entry => $"{entry.ClrType.Name} → {entry.Declaration!.ConflictPolicy}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0, $"""
            An immutable record declares a conflict policy that would overwrite it. AuditInterceptor
            refuses any update to an IImmutableRecord (§7 rule 7, ADR-012), so the sync receiver would
            throw the first time two nodes diverged on one of these rows.

            Use ConflictPolicy.AppendOnly — entries accumulate and nothing is overwritten (ADR-005) —
            or ManualReview, which leaves the local row alone.

            {string.Join(Environment.NewLine, offenders.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void The_tenant_filter_is_bypassed_only_where_it_is_meant_to_be()
    {
        // ITenantContext.BypassTenantFilter is deliberately awkward — it takes a reason string — but
        // awkwardness is not a control. Every read inside a bypass is unfiltered, so one missed
        // predicate writes a store's trading data into another tenant, and that is the single worst
        // failure the persistence layer can have. The control is this list.
        //
        // Every entry below has the same shape: a credential arrives carrying no tenant, and the
        // credential itself is what identifies the tenant. There is no ambient tenant to filter by
        // yet, so the lookup cannot be filtered — and each one matches on a unique, high-entropy
        // column, so exactly one row can come back.
        //
        // Note what is *not* here. ITenantContext's own doc comment predicted two Stage 04 callers,
        // the sync receiver and the backup job, and neither needed a bypass: the receiver re-scopes
        // to the batch's tenant, which keeps the global filter working, and the backup job dumps
        // through pg_dump without issuing a query at all. The prediction was wrong in the safe
        // direction, and this test is how that stays true.
        IReadOnlyList<string> violations = SolutionSource.FindViolations(
            line => line.Contains("BypassTenantFilter(", StringComparison.Ordinal),
            // The port's own declaration, and the two implementations of it.
            "src/VumaRetail.Application/Abstractions/ITenantContext.cs",
            "src/VumaRetail.Infrastructure/Security/AmbientTenantContext.cs",
            "src/VumaRetail.Infrastructure/Persistence/DesignTimeDbContextFactory.cs",
            // A terminal presenting a client certificate, and a refresh token arriving at
            // /auth/refresh. Both are pre-authentication lookups keyed on a unique digest — the
            // thumbprint and the token hash — which is how the tenant is discovered in the first
            // place. Stage 02, ADR-040.
            "src/VumaRetail.Infrastructure/Persistence/Repositories/IdentityRepositories.cs",
            // The migration runner creates a fixed tenant context for each selected company.
            // Its explicit implementation is an adapter, not a cross-tenant query scope.
            "src/VumaRetail.Infrastructure/Registry/CompanyMigrationServices.cs",
            // The demo seeder asking whether the demo tenant exists at all.
            "src/VumaRetail.StoreServer/DemoSeed.cs");

        Assert.True(violations.Count == 0, $"""
            Something opened a cross-tenant scope in a file not on the closed list. That is not
            forbidden, but it is not a decision to make quietly: every read inside the scope is
            unfiltered, so one missed predicate writes a store's trading data into another tenant.

            Before adding a file here, check whether the caller can re-scope to the tenant it is
            acting for instead — which is what Stage 04's sync receiver does, and why it is not on
            this list.

            {string.Join(Environment.NewLine, violations.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void Every_replicated_entity_can_be_materialised_by_the_receiver()
    {
        // The receiver creates a row it has never seen with Activator.CreateInstance(type, nonPublic).
        // An entity with no parameterless constructor would replicate perfectly until the first time
        // a peer sent one this node did not already have — which is precisely the case replication
        // exists for, and precisely the case no local test exercises.
        List<string> offenders = Model.GetEntityTypes()
            .Where(entityType => typeof(Entity).IsAssignableFrom(entityType.ClrType))
            .Where(entityType => entityType.ClrType.GetCustomAttribute<ReplicatedAttribute>()
                is { Scope: not ReplicationScope.NodeLocal })
            .Where(entityType => entityType.ClrType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: [],
                modifiers: null) is null)
            .Select(entityType => entityType.ClrType.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0, $"""
            A replicated entity has no parameterless constructor, so the sync receiver cannot
            materialise a row this node has not seen before. Add the private EF constructor every
            other entity has.

            {string.Join(Environment.NewLine, offenders.Select(entry => $"  - {entry}"))}
            """);
    }
}
