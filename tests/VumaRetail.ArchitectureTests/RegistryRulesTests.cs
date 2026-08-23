using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using VumaRetail.Domain.Entities;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Registry;

namespace VumaRetail.ArchitectureTests;

/// <summary>
/// Stage 06c's own persistence rules for <c>VumaRegistryDbContext</c> — the same conventions
/// <see cref="PersistenceRulesTests"/> already proves for <c>VumaRetailDbContext</c>, checked
/// separately because the registry is a different model entirely (ADR-099) and nothing would otherwise
/// notice if it drifted from the same conventions.
/// </summary>
public sealed class RegistryRulesTests
{
    private static readonly IModel Model = new RegistryDesignTimeDbContextFactory().CreateDbContext([]).Model;

    [Fact]
    public void Every_registry_entity_lives_in_the_registry_schema()
    {
        List<string> offenders = Model.GetEntityTypes()
            .Where(entityType => entityType.GetTableName() is not null)
            .Where(entityType => entityType.GetSchema() != Schemas.Registry)
            .Select(entityType => $"{entityType.ClrType.Name} → {entityType.GetSchema() ?? "public"}")
            .ToList();

        Assert.True(offenders.Count == 0, $"""
            Every table in VumaRegistryDbContext belongs to the registry schema — there is nowhere else
            for it to belong, since this database holds nothing but the registry (ADR-099).

            {string.Join(Environment.NewLine, offenders.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void Every_registry_entity_declares_how_it_replicates()
    {
        // Mirrors PersistenceRulesTests.Every_persisted_entity_declares_how_it_replicates, for the
        // registry's own model rather than VumaRetailDbContext's.
        List<string> offenders = Model.GetEntityTypes()
            .Where(entityType => typeof(Entity).IsAssignableFrom(entityType.ClrType))
            .Where(entityType => entityType.ClrType.GetCustomAttribute<ReplicatedAttribute>() is null)
            .Select(entityType => entityType.ClrType.FullName ?? entityType.ClrType.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0, $"""
            Every persisted registry entity declares [Replicated(scope, conflictPolicy)] (ADR-007).

            {string.Join(Environment.NewLine, offenders.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void Every_registry_entity_is_soft_delete_filtered()
    {
        List<string> offenders = Model.GetEntityTypes()
            .Where(entityType => typeof(Entity).IsAssignableFrom(entityType.ClrType))
            .Where(entityType => entityType.GetQueryFilter() is null)
            .Select(entityType => entityType.ClrType.Name)
            .ToList();

        Assert.True(offenders.Count == 0, $"""
            A registry entity has no query filter, so it is not soft-delete filtered (§7 rule 8).

            {string.Join(Environment.NewLine, offenders.Select(entry => $"  - {entry}"))}
            """);
    }

    [Fact]
    public void No_registry_entity_carries_a_foreign_key_into_a_company_database()
    {
        // There cannot be one — a foreign key is a same-database concept, and a company database is a
        // different database entirely (ADR-116) — but the meaningful assertion is that no registry
        // entity has a real relationship-based foreign key at all; every cross-entity reference here is
        // a bare Guid (CompanyGroupMember.MemberCompanyId, SagaLeg.CompanyId, and so on), exactly the
        // convention CONVENTIONS.md §2 already requires across schema boundaries generally.
        List<string> offenders = Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Select(fk => $"{fk.DeclaringEntityType.ClrType.Name} → {fk.PrincipalEntityType.ClrType.Name}")
            .ToList();

        Assert.True(offenders.Count == 0, $"""
            The registry model has a real EF-relationship foreign key. Reference the other row by a bare
            Guid instead — the registry has no cross-database transaction to protect a foreign key with
            anyway (ADR-116).

            {string.Join(Environment.NewLine, offenders.Select(entry => $"  - {entry}"))}
            """);
    }
}
