using Microsoft.EntityFrameworkCore;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Infrastructure.Persistence;

/// <summary>EF context for the per-tenant registry database (ADR-099).</summary>
public sealed class VumaRegistryDbContext(DbContextOptions<VumaRegistryDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyGroup> CompanyGroups => Set<CompanyGroup>();
    public DbSet<CompanyGroupMember> CompanyGroupMembers => Set<CompanyGroupMember>();
    public DbSet<SagaIntent> SagaIntents => Set<SagaIntent>();
    public DbSet<SagaLeg> SagaLegs => Set<SagaLeg>();
    public DbSet<RegistryOutboxMessage> Outbox => Set<RegistryOutboxMessage>();

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
        });

        modelBuilder.Entity<CompanyGroup>(b => { b.ToTable("company_groups", "registry"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique(); });
        modelBuilder.Entity<CompanyGroupMember>(b => { b.ToTable("company_group_members", "registry"); b.HasKey(x => new { x.GroupId, x.CompanyId }); });
        modelBuilder.Entity<SagaIntent>(b => { b.ToTable("saga_intents", "registry"); b.HasKey(x => x.Id); b.Property(x => x.Type).HasMaxLength(100).IsRequired(); b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired(); b.Property(x => x.Payload).IsRequired(); b.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique(); });
        modelBuilder.Entity<SagaLeg>(b => { b.ToTable("saga_legs", "registry"); b.HasKey(x => new { x.IntentId, x.LegId }); b.Property(x => x.LastError).HasMaxLength(1000); b.HasOne<SagaIntent>().WithMany(x => x.Legs).HasForeignKey(x => x.IntentId); });
        modelBuilder.Entity<RegistryOutboxMessage>(b => { b.ToTable("outbox", "registry"); b.HasKey(x => x.Id); b.Property(x => x.Type).HasMaxLength(100).IsRequired(); b.Property(x => x.Payload).IsRequired(); });

        base.OnModelCreating(modelBuilder);
    }
}
