using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Infrastructure.Persistence;

/// <summary>EF context for the per-tenant registry database (ADR-099).</summary>
public sealed class VumaRegistryDbContext(DbContextOptions<VumaRegistryDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Company> Companies => Set<Company>();

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

        base.OnModelCreating(modelBuilder);
    }
}
