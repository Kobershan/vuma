using Microsoft.EntityFrameworkCore;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence.Configurations.Registry;

namespace VumaRetail.Infrastructure.Persistence.Registry;

/// <summary>
/// The tenant's registry — companies, company groups, saga intents and legs, the registry outbox
/// (ADR-099). A separate physical database from every company's <see cref="VumaRetailDbContext"/>, with
/// its own connection string and its own migration chain, migrated first (ADR-117).
/// </summary>
/// <remarks>
/// <para>
/// No entity here carries a global tenant query filter the way <see cref="VumaRetailDbContext"/>'s
/// entities do. The registry database is already one tenant's; there is nothing to filter, the same way
/// a company database needs no tenant filter either — the database boundary <em>is</em> the isolation.
/// </para>
/// <para>
/// Nothing here writes to a company database and nothing in a company database writes here directly —
/// there is no shared connection, no linked server, nothing that could be mistaken for a cross-database
/// transaction (ADR-116). The only path between the two is <see cref="RegistryOutboxMessage"/>, read and
/// written by application code on each side.
/// </para>
/// </remarks>
public sealed class VumaRegistryDbContext : DbContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="options">EF options, including the Npgsql provider.</param>
    public VumaRegistryDbContext(DbContextOptions<VumaRegistryDbContext> options)
        : base(options)
    {
    }

    /// <summary>Every company this tenant has provisioned or is provisioning.</summary>
    public DbSet<RegistryCompany> Companies => Set<RegistryCompany>();

    /// <summary>Named sets of companies — the container consolidation and group scope operate over.</summary>
    public DbSet<CompanyGroup> CompanyGroups => Set<CompanyGroup>();

    /// <summary>One company's membership of one group.</summary>
    public DbSet<CompanyGroupMember> CompanyGroupMembers => Set<CompanyGroupMember>();

    /// <summary>An immutable record of a cross-database operation, before any leg is dispatched.</summary>
    public DbSet<SagaIntent> SagaIntents => Set<SagaIntent>();

    /// <summary>One company's share of a saga intent.</summary>
    public DbSet<SagaLeg> SagaLegs => Set<SagaLeg>();

    /// <summary>A change waiting to reach a company database.</summary>
    public DbSet<RegistryOutboxMessage> OutboxMessages => Set<RegistryOutboxMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Configurations are applied one by one, not by assembly scan: this context lives in the same
        // assembly as VumaRetailDbContext's own configurations, and a scan would pull every company
        // entity's mapping into the registry's model too.
        modelBuilder.ApplyConfiguration(new RegistryCompanyConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyGroupConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyGroupMemberConfiguration());
        modelBuilder.ApplyConfiguration(new SagaIntentConfiguration());
        modelBuilder.ApplyConfiguration(new SagaLegConfiguration());
        modelBuilder.ApplyConfiguration(new RegistryOutboxMessageConfiguration());

        ApplySoftDeleteFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Excludes soft-deleted rows (§7 rule 8), the same way <see cref="VumaRetailDbContext"/> does — but
    /// with no tenant clause, because this database already belongs to exactly one tenant and there is
    /// nothing left to filter.
    /// </summary>
    private static void ApplySoftDeleteFilters(ModelBuilder modelBuilder)
    {
        foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(Domain.Entities.Entity).IsAssignableFrom(entityType.ClrType) || entityType.BaseType is not null)
            {
                continue;
            }

            System.Reflection.MethodInfo filter = typeof(VumaRegistryDbContext)
                .GetMethod(nameof(BuildSoftDeleteFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(entityType.ClrType);

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter((System.Linq.Expressions.LambdaExpression)filter.Invoke(null, null)!);
        }
    }

    private static System.Linq.Expressions.Expression<Func<TEntity, bool>> BuildSoftDeleteFilter<TEntity>()
        where TEntity : Domain.Entities.Entity
        => entity => entity.DeletedAt == null;
}
