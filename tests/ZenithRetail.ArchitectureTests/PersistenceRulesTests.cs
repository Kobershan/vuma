using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ZenithRetail.Domain.Entities;
using ZenithRetail.Infrastructure.Persistence;

namespace ZenithRetail.ArchitectureTests;

/// <summary>
/// The Stage 01 persistence rules, enforced now because they become unenforceable later.
/// </summary>
/// <remarks>
/// Each of these is cheap to obey while there are three tables and expensive to retrofit across
/// forty modules. A cross-schema foreign key added in Stage 12 and found in Stage 29 is not a fix,
/// it is a migration of a year's trading data.
/// </remarks>
public sealed class PersistenceRulesTests
{
    private static readonly IModel Model = new DesignTimeDbContextFactory().CreateDbContext([]).Model;

    [Fact]
    public void No_foreign_key_crosses_a_schema_boundary()
    {
        // CONVENTIONS.md §2 / ADR-010. A foreign key across a module boundary is the single thing
        // that makes the modular monolith unextractable — the database will refuse to let the
        // modules be separated, whatever the code says. Modules reference each other by ID and
        // resolve through published contracts or domain events.
        List<string> offenders = [];

        foreach (IEntityType entityType in Model.GetEntityTypes())
        {
            string? schema = entityType.GetSchema();

            foreach (IForeignKey foreignKey in entityType.GetForeignKeys())
            {
                string? principalSchema = foreignKey.PrincipalEntityType.GetSchema();

                if (schema is not null && principalSchema is not null
                    && !string.Equals(schema, principalSchema, StringComparison.Ordinal))
                {
                    offenders.Add(
                        $"{schema}.{entityType.GetTableName()} → {principalSchema}.{foreignKey.PrincipalEntityType.GetTableName()}");
                }
            }
        }

        Assert.True(offenders.Count == 0, $"""
            A foreign key crosses a module schema boundary (CONVENTIONS.md §2, ADR-010). Reference the
            other module by ID and resolve through its published contract or a domain event instead.

            {string.Join(Environment.NewLine, offenders.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void Every_persisted_entity_lives_in_a_declared_module_schema()
    {
        // A table with no schema lands in `public`, which belongs to no module and so belongs to
        // everyone. That is how the boundaries erode.
        IReadOnlyCollection<string> declared = typeof(Schemas)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, FieldType: var type } && type == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        List<string> offenders = Model.GetEntityTypes()
            .Where(entityType => entityType.GetTableName() is not null)
            .Where(entityType => entityType.GetSchema() is not { } schema || !declared.Contains(schema))
            .Select(entityType => $"{entityType.ClrType.Name} → {entityType.GetSchema() ?? "public"}")
            .ToList();

        Assert.True(offenders.Count == 0, $"""
            Every table belongs to exactly one module schema, and the name is a constant on
            ZenithRetail.Infrastructure.Persistence.Schemas (ADR-010).

            {string.Join(Environment.NewLine, offenders.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void Every_persisted_entity_declares_how_it_replicates()
    {
        // ADR-007: an entity whose conflict behaviour nobody chose is an entity that will lose
        // somebody's data quietly. Declared next to the entity, in the diff that creates it, so
        // Stage 04's sync engine inherits a complete registry rather than a retrofitted one.
        List<string> offenders = Model.GetEntityTypes()
            .Where(entityType => typeof(Entity).IsAssignableFrom(entityType.ClrType))
            .Where(entityType => entityType.ClrType.GetCustomAttribute<ReplicatedAttribute>() is null)
            .Select(entityType => entityType.ClrType.FullName ?? entityType.ClrType.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0, $"""
            Every persisted entity declares [Replicated(scope, conflictPolicy)] (ADR-007). Choose
            deliberately — ReplicationScope.NodeLocal is a valid answer, and silence is not.

            {string.Join(Environment.NewLine, offenders.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void SaveChanges_is_called_only_from_the_persistence_layer()
    {
        // CLAUDE.md §7 rule 2: every write goes through a command handler, and the Stage 03 pipeline
        // owns the commit. An endpoint that saves for itself skips validation, the audit stamp, the
        // outbox write and — from Stage 04b — the read-only interceptor.
        IReadOnlyList<string> violations = SolutionSource.FindViolations(
            line => line.Contains("SaveChanges(", StringComparison.Ordinal)
                || line.Contains("SaveChangesAsync(", StringComparison.Ordinal),
            "src/ZenithRetail.Infrastructure/Persistence/ZenithRetailDbContext.cs");

        Assert.True(violations.Count == 0, $"""
            SaveChanges belongs to the persistence layer (CLAUDE.md §7 rule 2). Mutate tracked
            entities in a command handler and let the pipeline commit through IUnitOfWork.

            {string.Join(Environment.NewLine, violations.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void Nothing_reads_the_wall_clock_except_SystemClock()
    {
        // CONVENTIONS.md §6. The licensing ladder runs over 45 days, a lease expires in 72 hours and
        // an accounting period closes on a boundary — none of which can be tested by waiting. Take an
        // IClock and the test controls time.
        IReadOnlyList<string> violations = SolutionSource.FindViolations(
            line => line.Contains("DateTime.Now", StringComparison.Ordinal)
                || line.Contains("DateTime.UtcNow", StringComparison.Ordinal)
                || line.Contains("DateTimeOffset.Now", StringComparison.Ordinal)
                || line.Contains("DateTimeOffset.UtcNow", StringComparison.Ordinal),
            "src/ZenithRetail.Infrastructure/Time/SystemClock.cs",
            // UuidV7 is the one exemption, and it is a real one rather than a convenience. A UUID v7
            // is time-ordered by construction (ADR-004), and Domain references nothing (§7 rule 1),
            // so it cannot take an IClock. The timestamp inside a key is an ordering device, not a
            // business fact — no rule ever reads it — and the explicit NewGuid(DateTimeOffset)
            // overload is there for the tests that need a controlled one.
            "src/ZenithRetail.Domain/Primitives/UuidV7.cs");

        Assert.True(violations.Count == 0, $"""
            Only SystemClock reads the wall clock (CONVENTIONS.md §6). Inject IClock instead.

            {string.Join(Environment.NewLine, violations.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void Every_entity_is_soft_deletable_and_tenant_filtered()
    {
        // Both filters are applied by convention rather than per configuration, so this asserts the
        // convention actually reached every entity. The failure mode of a missed tenant filter is one
        // tenant reading another's trading data.
        List<string> offenders = Model.GetEntityTypes()
            .Where(entityType => typeof(Entity).IsAssignableFrom(entityType.ClrType))
            .Where(entityType => entityType.GetQueryFilter() is null)
            .Select(entityType => entityType.ClrType.Name)
            .ToList();

        Assert.True(offenders.Count == 0, $"""
            An entity has no global query filter, so it is neither soft-delete filtered (§7 rule 8)
            nor tenant filtered (§7 rule 3).

            {string.Join(Environment.NewLine, offenders.Select(entry => $"  - {entry}"))}
            """);
    }
}
