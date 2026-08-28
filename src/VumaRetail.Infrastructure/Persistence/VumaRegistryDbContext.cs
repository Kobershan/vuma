using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Infrastructure.Persistence;

/// <summary>EF context for the per-tenant registry database (ADR-099).</summary>
public sealed class VumaRegistryDbContext(DbContextOptions<VumaRegistryDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyGroup> CompanyGroups => Set<CompanyGroup>();
    public DbSet<CompanyGroupMember> CompanyGroupMembers => Set<CompanyGroupMember>();
    public DbSet<SagaIntent> SagaIntents => Set<SagaIntent>();
    public DbSet<SagaLeg> SagaLegs => Set<SagaLeg>();
    public DbSet<RegistryOutboxMessage> RegistryOutboxMessages => Set<RegistryOutboxMessage>();

    public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        => SaveChangesAsync(cancellationToken);

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = await Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        TResult result = await operation(cancellationToken).ConfigureAwait(false);
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(builder =>
        {
            builder.ToTable("companies", "registry");
            builder.HasKey(company => company.Id);
            builder.Property(company => company.TenantId).IsRequired();
            builder.Property(company => company.Code).HasMaxLength(32).IsRequired();
            builder.Property(company => company.LegalName).HasMaxLength(200).IsRequired();
            builder.Property(company => company.TradingName).HasMaxLength(200).IsRequired();
            builder.Property(company => company.RegistrationNumber).HasMaxLength(100);
            builder.Property(company => company.TaxNumber).HasMaxLength(100);
            builder.Property(company => company.BaseCurrency).HasMaxLength(3).IsRequired();
            builder.Property(company => company.Locale).HasMaxLength(35).IsRequired();
            builder.Property(company => company.DocumentPrefix).HasMaxLength(32).IsRequired();
            builder.Property(company => company.ConnectionSecretRef).HasMaxLength(512);
            builder.Property(company => company.MigrationState).HasMaxLength(32).IsRequired();
            builder.Property(company => company.LifecycleState).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.HasIndex(company => new { company.TenantId, company.Code }).IsUnique();
            builder.HasIndex(company => new { company.TenantId, company.DocumentPrefix }).IsUnique();
            builder.HasIndex(company => new { company.TenantId, company.IsActive });
            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_companies_lifecycle_state",
                    "lifecycle_state IN ('Provisioning', 'Seeding', 'Registered', 'Active', 'Deactivated')");
                table.HasCheckConstraint(
                    "ck_companies_active_requires_active_state",
                    "NOT is_active OR lifecycle_state = 'Active'");
                table.HasCheckConstraint(
                    "ck_companies_active_requires_secret",
                    "NOT is_active OR connection_secret_ref IS NOT NULL");
            });
        });

        modelBuilder.Entity<CompanyGroup>(builder =>
        {
            builder.ToTable("company_groups", "registry"); builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever(); builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
            builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            builder.HasAlternateKey(x => new { x.TenantId, x.Id });
            builder.HasMany(x => x.Members).WithOne().HasForeignKey(x => new { x.TenantId, x.GroupId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CompanyGroupMember>(builder =>
        {
            builder.ToTable("company_group_members", "registry"); builder.HasKey(x => new { x.GroupId, x.CompanyId });
            builder.Property(x => x.GroupId).ValueGeneratedNever(); builder.Property(x => x.CompanyId).ValueGeneratedNever(); builder.Property(x => x.TenantId).IsRequired();
            builder.HasOne<Company>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<SagaIntent>(builder =>
        {
            builder.ToTable("saga_intents", "registry"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Type).HasMaxLength(128).IsRequired(); builder.Property(x => x.IdempotencyKey).HasMaxLength(256).IsRequired();
            builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired(); builder.Property(x => x.State).HasConversion<string>().HasMaxLength(16).IsRequired();
            builder.Property(x => x.Owner).HasMaxLength(128); builder.Property(x => x.InitiatedBy).HasMaxLength(128).IsRequired(); builder.Property(x => x.OperationStamp).HasMaxLength(128).IsRequired();
            builder.HasAlternateKey(x => new { x.TenantId, x.Id });
            builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique(); builder.HasIndex(x => new { x.TenantId, x.State, x.CreatedAt });
            builder.HasMany(x => x.Legs).WithOne().HasForeignKey(x => new { x.TenantId, x.IntentId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SagaLeg>(builder =>
        {
            builder.ToTable("saga_legs", "registry"); builder.HasKey(x => new { x.IntentId, x.LegId });
            builder.Property(x => x.IntentId).ValueGeneratedNever(); builder.Property(x => x.LegId).ValueGeneratedNever(); builder.Property(x => x.TenantId).IsRequired(); builder.Property(x => x.CompanyId).IsRequired(); builder.Property(x => x.OperationStamp).HasMaxLength(128).IsRequired();
            builder.Property(x => x.State).HasConversion<string>().HasMaxLength(16).IsRequired(); builder.Property(x => x.LastError).HasMaxLength(1024);
            builder.HasOne<Company>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(x => new { x.CompanyId, x.State });
        });
        modelBuilder.Entity<RegistryOutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages", "registry"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Type).HasMaxLength(128).IsRequired(); builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired(); builder.Property(x => x.IdempotencyKey).HasMaxLength(256).IsRequired(); builder.Property(x => x.OperationStamp).HasMaxLength(128).IsRequired();
            builder.Property(x => x.Attempts).IsRequired(); builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique(); builder.HasIndex(x => new { x.TenantId, x.DispatchedAt, x.CreatedAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}
