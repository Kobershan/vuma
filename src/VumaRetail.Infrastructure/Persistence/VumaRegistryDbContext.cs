using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence.Configurations;

namespace VumaRetail.Infrastructure.Persistence;

/// <summary>EF context for the per-tenant registry database (ADR-099).</summary>
public sealed class VumaRegistryDbContext(
    DbContextOptions<VumaRegistryDbContext> options,
    ITenantContext tenantContext) : DbContext(options), IUnitOfWork
{
    private readonly ITenantContext _tenantContext = tenantContext;

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyLifecycleAudit> CompanyLifecycleAudits => Set<CompanyLifecycleAudit>();
    public DbSet<CompanyGroup> CompanyGroups => Set<CompanyGroup>();
    public DbSet<CompanyGroupMember> CompanyGroupMembers => Set<CompanyGroupMember>();
    public DbSet<SagaIntent> SagaIntents => Set<SagaIntent>();
    public DbSet<SagaLeg> SagaLegs => Set<SagaLeg>();
    public DbSet<RegistryOutboxMessage> RegistryOutboxMessages => Set<RegistryOutboxMessage>();

    // Stage 06d: Group services
    public DbSet<CreditGroup> CreditGroups => Set<CreditGroup>();
    public DbSet<CreditGroupMember> CreditGroupMembers => Set<CreditGroupMember>();
    public DbSet<CreditHold> CreditHolds => Set<CreditHold>();
    public DbSet<CreditExposureEntry> CreditExposureEntries => Set<CreditExposureEntry>();
    public DbSet<CatalogRoutingIndexEntry> CatalogRoutingIndex => Set<CatalogRoutingIndexEntry>();

    // Stage 06e: Trading group
    public DbSet<CompanyLink> CompanyLinks => Set<CompanyLink>();
    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<Premises> Premises => Set<Premises>();
    public DbSet<PremisesOccupancy> PremisesOccupancies => Set<PremisesOccupancy>();
    public DbSet<PremisesBinLayout> PremisesBinLayouts => Set<PremisesBinLayout>();
    public DbSet<RegistryUser> RegistryUsers => Set<RegistryUser>();
    public DbSet<RegistryUserCompanyAccess> RegistryUserCompanyAccesses => Set<RegistryUserCompanyAccess>();
    public DbSet<RegistryTerminal> Terminals => Set<RegistryTerminal>();

    // Stage 07c: Cross-company money
    public DbSet<GroupReceipt> GroupReceipts => Set<GroupReceipt>();
    public DbSet<GroupPaymentRun> GroupPaymentRuns => Set<GroupPaymentRun>();
    public DbSet<InterCompanyClearingIntent> InterCompanyClearingIntents => Set<InterCompanyClearingIntent>();
    public DbSet<GroupReceiptAllocation> GroupReceiptAllocations => Set<GroupReceiptAllocation>();
    public DbSet<GroupPaymentAllocation> GroupPaymentAllocations => Set<GroupPaymentAllocation>();
    public DbSet<InterCompanyClearingLeg> InterCompanyClearingLegs => Set<InterCompanyClearingLeg>();

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
            builder.Property(company => company.ProvisioningStep).HasMaxLength(64).IsRequired();
            builder.Property(company => company.ProvisioningError).HasMaxLength(256);
            builder.Property(company => company.ProvisioningAttempts).IsRequired();
            builder.Property(company => company.OperatorId).IsRequired();
            builder.HasIndex(company => new { company.TenantId, company.OperatorId })
                .HasDatabaseName("ix_companies_tenant_id_operator_id");
            builder.Property(company => company.DeactivatedBy).HasMaxLength(256);
            builder.Property(company => company.DeactivationReason).HasMaxLength(500);
            builder.Property(company => company.LifecycleState).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.HasAlternateKey(company => new { company.TenantId, company.Id });
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

        modelBuilder.Entity<CompanyLifecycleAudit>(builder =>
        {
            builder.ToTable("company_lifecycle_audits", "registry");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Actor).HasMaxLength(256).IsRequired();
            builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            builder.Property(x => x.FromState).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.Property(x => x.ToState).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.HasIndex(x => new { x.TenantId, x.CompanyId, x.OccurredAt });
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
            builder.ToTable("company_group_members", "registry"); builder.HasKey(x => new { x.TenantId, x.GroupId, x.CompanyId });
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
            builder.ToTable("saga_legs", "registry"); builder.HasKey(x => new { x.TenantId, x.IntentId, x.LegId });
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

        // Stage 06d: Group services
        modelBuilder.Entity<CreditGroup>(builder =>
        {
            builder.ToTable("credit_groups", "registry"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Name).HasMaxLength(128).IsRequired(); builder.Property(x => x.Direction).HasMaxLength(16).IsRequired(); builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            builder.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.CreditGroupId).OnDelete(DeleteBehavior.Cascade);
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<CreditGroupMember>(builder =>
        {
            builder.ToTable("credit_group_members", "registry"); builder.HasKey(x => new { x.TenantId, x.CreditGroupId, x.CompanyId });
            builder.Property(x => x.CreditGroupId).ValueGeneratedNever(); builder.Property(x => x.CompanyId).ValueGeneratedNever();
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<CreditHold>(builder =>
        {
            builder.ToTable("credit_holds", "registry"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Currency).HasMaxLength(3).IsRequired(); builder.Property(x => x.DocumentReference).HasMaxLength(256).IsRequired();
            builder.Property(x => x.State).HasConversion<string>().HasMaxLength(16).IsRequired();
            builder.HasIndex(x => new { x.TenantId, x.CreditGroupId, x.State });
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<CreditExposureEntry>(builder =>
        {
            builder.ToTable("credit_exposure_entries", "registry"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Currency).HasMaxLength(3).IsRequired(); builder.Property(x => x.DocumentReference).HasMaxLength(256).IsRequired();
            builder.HasIndex(x => new { x.TenantId, x.CreditGroupId, x.CompanyId });
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<CatalogRoutingIndexEntry>(builder =>
        {
            builder.ToTable("catalog_routing_index", "registry"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Barcode).HasMaxLength(64).IsRequired(); builder.Property(x => x.CompanyCode).HasMaxLength(32).IsRequired(); builder.Property(x => x.ItemCode).HasMaxLength(64).IsRequired();
            builder.HasIndex(x => new { x.TenantId, x.Barcode }).IsUnique(); builder.HasIndex(x => new { x.TenantId, x.CompanyId, x.Barcode });
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });

        // Stage 06e: Trading group. Configured inline, here, like every other registry
        // entity above — never as standalone IEntityTypeConfiguration classes. The business
        // context scans this assembly for configurations (VumaRetailDbContext.OnModelCreating),
        // so a standalone registry configuration is silently applied to the company database
        // too, creating registry tables on the wrong migration chain.
        modelBuilder.Entity<CompanyLink>(builder =>
        {
            builder.ToTable("company_links", "registry");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.OperatorId).IsRequired();
            builder.Property(x => x.OperatorName).HasMaxLength(256);
            builder.Property(x => x.CompanyAId).IsRequired();
            builder.Property(x => x.CompanyBId).IsRequired();
            builder.Property(x => x.Scopes).IsRequired();
            builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
            builder.Property(x => x.EffectiveFrom).IsRequired();
            builder.Property(x => x.EffectiveTo);
            builder.Property(x => x.AcceptedByA);
            builder.Property(x => x.AcceptedByB);
            builder.Property(x => x.AcceptedByABy).HasMaxLength(128);
            builder.Property(x => x.AcceptedByBBy).HasMaxLength(128);
            builder.Property(x => x.AcceptedByAAt);
            builder.Property(x => x.AcceptedByBAt);
            builder.Property(x => x.AcceptedByAFingerprint).HasMaxLength(128);
            builder.Property(x => x.AcceptedByBFingerprint).HasMaxLength(128);
            builder.Property(x => x.AcceptedAt);
            builder.Property(x => x.SuspendedReason).HasMaxLength(500);
            builder.Property(x => x.SuspendedAt);
            builder.Property(x => x.RevokedReason).HasMaxLength(500);
            builder.Property(x => x.RevokedAt);
            builder.HasIndex(x => new { x.TenantId, x.CompanyAId, x.CompanyBId }).IsUnique();
            builder.HasIndex(x => new { x.TenantId, x.Status });
            builder.HasIndex(x => new { x.TenantId, x.OperatorId });
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Operator>(builder =>
        {
            builder.ToTable("operators", "registry");
            builder.HasKey(x => x.OperatorId);
            builder.Property(x => x.OperatorId).ValueGeneratedNever();
            builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
            builder.Property(x => x.LicenceFingerprint).HasMaxLength(128).IsRequired();
            builder.Property(x => x.IsActive).IsRequired();
            builder.HasIndex(x => new { x.TenantId, x.IsActive });
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Premises>(builder =>
        {
            builder.ToTable("premises", "registry");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
            builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
            builder.Property(x => x.Address).HasMaxLength(500).IsRequired();
            builder.Property(x => x.GeoLocation).HasMaxLength(256).IsRequired();
            builder.Property(x => x.TradingHours).HasMaxLength(128);
            builder.Property(x => x.IsActive).IsRequired();
            builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            builder.HasIndex(x => new { x.TenantId, x.IsActive });
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<PremisesOccupancy>(builder =>
        {
            builder.ToTable("premises_occupancies", "registry");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.PremisesId).IsRequired();
            builder.Property(x => x.CompanyId).IsRequired();
            builder.Property(x => x.StoreId).IsRequired();
            builder.Property(x => x.OccupiesFrom).IsRequired();
            builder.Property(x => x.OccupiesTo);
            builder.HasIndex(x => new { x.PremisesId, x.CompanyId });
            builder.HasIndex(x => new { x.TenantId, x.PremisesId });
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<PremisesBinLayout>(builder =>
        {
            builder.ToTable("premises_bin_layouts", "registry");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.PremisesId).IsRequired();
            builder.Property(x => x.ZoneCode).HasMaxLength(32).IsRequired();
            builder.Property(x => x.BinCode).HasMaxLength(32).IsRequired();
            builder.Property(x => x.Description).HasMaxLength(500);
            builder.Property(x => x.IsShared).IsRequired();
            builder.HasIndex(x => new { x.PremisesId, x.ZoneCode, x.BinCode });
            builder.HasIndex(x => new { x.TenantId, x.PremisesId });
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<RegistryUser>(builder =>
        {
            builder.ToTable("registry_users", "registry");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.Login).HasMaxLength(128).IsRequired();
            builder.Property(x => x.ContactDetails).HasMaxLength(500);
            builder.Property(x => x.OperatorId).IsRequired();
            builder.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
            builder.Property(x => x.IsEnabled).IsRequired();
            builder.HasIndex(x => new { x.TenantId, x.Login }).IsUnique();
            builder.HasIndex(x => new { x.TenantId, x.OperatorId });
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<RegistryUserCompanyAccess>(builder =>
        {
            builder.ToTable("user_company_access", "registry");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.RegistryUserId).IsRequired();
            builder.Property(x => x.CompanyId).IsRequired();
            builder.Property(x => x.Roles).HasMaxLength(500).IsRequired();
            builder.Property(x => x.GrantedBy).HasMaxLength(128).IsRequired();
            builder.Property(x => x.GrantedAt).IsRequired();

            builder.HasIndex(x => new { x.RegistryUserId, x.CompanyId });
            builder.HasIndex(x => new { x.TenantId, x.RegistryUserId });
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<RegistryTerminal>(builder =>
        {
            builder.ToTable("terminals", "registry");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.PremisesId).IsRequired();
            builder.Property(x => x.TerminalId).HasMaxLength(128).IsRequired();
            builder.Property(x => x.DeviceCertThumbprint).HasMaxLength(64).IsRequired();
            builder.Property(x => x.IsActive).IsRequired();
            builder.Property(x => x.CompanyIds)
                .HasConversion(
                    v => v.ToArray(),
                    v => v.ToList());
            builder.HasIndex(x => new { x.TenantId, x.TerminalId }).IsUnique();
            builder.HasIndex(x => new { x.TenantId, x.PremisesId });
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });

        // Stage 07c: Cross-company money
        modelBuilder.Entity<GroupReceipt>(builder =>
        {
            builder.ToTable("group_receipts", "registry");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.CapturingCompanyId).IsRequired();
            builder.Property(x => x.BankAccountId).IsRequired();
            builder.Property(x => x.TenderType).HasMaxLength(32).IsRequired();
            builder.Property(x => x.Reference).HasMaxLength(256).IsRequired();
            builder.Property(x => x.CapturedAt).IsRequired();
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            builder.HasMoney(x => x.Amount, "amount");
            builder.HasIndex(x => new { x.TenantId, x.Status });
            builder.HasIndex(x => new { x.TenantId, x.CapturedAt });
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<GroupReceiptAllocation>(builder =>
        {
            builder.ToTable("group_receipt_allocations", "registry");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.GroupReceiptId).IsRequired();
            builder.Property(x => x.CompanyId).IsRequired();
            builder.HasMoney(x => x.Amount, "amount");
            builder.Property(x => x.LegState).HasConversion<string>().HasMaxLength(24).IsRequired();
            builder.Property(x => x.TargetInvoiceIds).HasConversion<string>();
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<GroupPaymentRun>(builder =>
        {
            builder.ToTable("group_payment_runs", "registry");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.CapturingCompanyId).IsRequired();
            builder.Property(x => x.BankAccountId).IsRequired();
            builder.Property(x => x.TenderType).HasMaxLength(32).IsRequired();
            builder.Property(x => x.Reference).HasMaxLength(256).IsRequired();
            builder.Property(x => x.PaidAt).IsRequired();
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            builder.HasMoney(x => x.Amount, "amount");
            builder.HasIndex(x => new { x.TenantId, x.Status });
            builder.HasIndex(x => new { x.TenantId, x.PaidAt });
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<GroupPaymentAllocation>(builder =>
        {
            builder.ToTable("group_payment_allocations", "registry");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.GroupPaymentRunId).IsRequired();
            builder.Property(x => x.CompanyId).IsRequired();
            builder.HasMoney(x => x.Amount, "amount");
            builder.Property(x => x.LegState).HasConversion<string>().HasMaxLength(24).IsRequired();
            builder.Property(x => x.TargetInvoiceIds).HasConversion<string>();
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<InterCompanyClearingIntent>(builder =>
        {
            builder.ToTable("inter_company_clearing_intents", "registry");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.GroupDocumentId).IsRequired();
            builder.Property(x => x.GroupDocumentType).HasMaxLength(64).IsRequired();
            builder.Property(x => x.FromCompanyId).IsRequired();
            builder.Property(x => x.ToCompanyId).IsRequired();
            builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            builder.Property(x => x.State).HasConversion<string>().HasMaxLength(24).IsRequired();
            builder.HasMoney(x => x.Amount, "amount");
            builder.Property(x => x.CreatedAt).IsRequired();
            builder.HasIndex(x => new { x.TenantId, x.State });
            builder.HasIndex(x => new { x.TenantId, x.GroupDocumentId });
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<InterCompanyClearingLeg>(builder =>
        {
            builder.ToTable("inter_company_clearing_legs", "registry");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.IntentId).IsRequired();
            builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.CompanyId).IsRequired();
            builder.HasMoney(x => x.Amount, "amount");
            builder.Property(x => x.Direction).HasMaxLength(16).IsRequired();
            builder.Property(x => x.State).HasConversion<string>().HasMaxLength(24).IsRequired();
            builder.HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        });

        // Registry rows are tenant-scoped just like company-database rows. Administrative callers
        // that genuinely span tenants must open the same explicit, logged bypass scope used by the
        // company context; an unresolved tenant therefore matches nothing by default.
        modelBuilder.Entity<Company>().HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<CompanyGroup>().HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<CompanyGroupMember>().HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<SagaIntent>().HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<SagaLeg>().HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<RegistryOutboxMessage>().HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<CompanyLifecycleAudit>().HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<GroupReceipt>().HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<GroupPaymentRun>().HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<InterCompanyClearingIntent>().HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<InterCompanyClearingLeg>().HasQueryFilter(x => IsTenantFilterBypassed || x.TenantId == CurrentTenantId);

        base.OnModelCreating(modelBuilder);
    }

    private Guid CurrentTenantId => _tenantContext.TenantId;
    private bool IsTenantFilterBypassed => _tenantContext.IsFilterBypassed;
}
