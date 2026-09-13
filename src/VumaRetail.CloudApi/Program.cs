using Microsoft.EntityFrameworkCore;
using VumaRetail.Infrastructure.Backup;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Security.Identity;
using VumaRetail.Infrastructure.Sync;
using VumaRetail.Web;
using VumaRetail.Web.Api;
using VumaRetail.Web.Diagnostics;
using VumaRetail.Web.Identity;
using VumaRetail.Web.Registry;
using VumaRetail.Web.Sync;
using VumaRetail.Web.Security;
using VumaRetail.Web.Dashboard;
using VumaRetail.Web.Inventory;
using VumaRetail.Web.Quality;
using VumaRetail.Web.Ecommerce;
using VumaRetail.Web.Service;
using VumaRetail.Web.Reporting;
using VumaRetail.PublicApi.Loyalty;

// The cloud tier: the replica of every store, tenant-keyed, and the backup vault's home. Source of
// truth for the tenant roll-up (R2).
//
// It is the same wiring as the store server with two deliberate differences. It has no host tenant —
// so every request must carry its own and a batch for the wrong tenant is refused rather than
// silently landing in whichever tenant the host happened to be pinned to. And it runs no outbox
// dispatcher: the cloud receives, and fanning a change back out to a store's siblings is a relay
// that has nothing to relay until Stage 06 builds master data worth sharing.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddVumaLogging("VumaRetail.CloudApi");

JwtOptions jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
HostTenantOptions host = builder.Configuration.GetSection(HostTenantOptions.SectionName).Get<HostTenantOptions>()
    ?? new HostTenantOptions();

if (jwt.UsesPlaceholderKey && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        $"{JwtOptions.SectionName}:SigningKey is still the development placeholder. Set a real signing "
        + "key before running outside Development.");
}

// A pinned tenant on the cloud tier would mean every store's batches landing in one tenant's data.
// The store server may be pinned, because it serves one shop; this host may not.
if (host.TenantId != Guid.Empty)
{
    throw new InvalidOperationException(
        $"{HostTenantOptions.SectionName}:TenantId must be empty on the cloud tier. This host serves "
        + "every tenant and resolves one per request; pinning it would let one store's batch be "
        + "written into another tenant's data.");
}

ProductionSecurityGuard.Validate(builder.Configuration, builder.Environment, cloudHost: true);

string connectionString = builder.Configuration.GetConnectionString("Vuma")
    ?? throw new InvalidOperationException("ConnectionStrings:Vuma is not configured.");
string registryConnectionString = builder.Configuration.GetConnectionString("Registry")
    ?? throw new InvalidOperationException("ConnectionStrings:Registry is not configured.");

NodeIdentityOptions node = builder.Configuration.GetSection(NodeIdentityOptions.SectionName)
    .Get<NodeIdentityOptions>() ?? new NodeIdentityOptions();

BackupVaultOptions vault = builder.Configuration.GetSection(BackupVaultOptions.SectionName)
    .Get<BackupVaultOptions>() ?? new BackupVaultOptions();

SnapshotEncryptionOptions encryption = builder.Configuration
    .GetSection(SnapshotEncryptionOptions.SectionName)
    .Get<SnapshotEncryptionOptions>() ?? new SnapshotEncryptionOptions();

PostgresBackupOptions postgres = builder.Configuration.GetSection(PostgresBackupOptions.SectionName)
    .Get<PostgresBackupOptions>() ?? new PostgresBackupOptions();

builder.Services.AddVumaWeb(jwt, host);
// Register the control-plane database first. AddVumaPersistence is intentionally last so the
// ordinary business pipeline keeps VumaRetailDbContext as its default IUnitOfWork while registry
// services resolve VumaRegistryDbContext explicitly.
builder.Services.AddVumaRegistryPersistence(registryConnectionString);
builder.Services.AddVumaPersistence(connectionString);
// Cloud serves scoped read surfaces as well as replication. These registrations are deliberately
// limited to the shared catalogue/CRM/loyalty/inventory contracts; writes still pass permissions,
// tenant filters and the company-routing boundary.
builder.Services.AddVumaCatalog();
builder.Services.AddVumaPartners();
builder.Services.AddVumaCrm();
builder.Services.AddVumaInventory();
builder.Services.AddVumaLoyalty(useFakeOrbit: false);
builder.Services.AddVumaQuality();
builder.Services.AddVumaEcommerce();
builder.Services.AddVumaServiceManagement();
builder.Services.AddVumaProjects();
builder.Services.AddVumaReporting();
builder.Services.AddSingleton(
    builder.Configuration.GetSection("Vuma:Loyalty:Public").Get<LoyaltyPublicOptions>()
        ?? new LoyaltyPublicOptions());
builder.Services.AddVumaSync(node);
builder.Services.AddVumaBackup(connectionString, vault, encryption, postgres);

WebApplication app = builder.Build();

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    using IServiceScope scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<VumaRegistryDbContext>().Database.MigrateAsync().ConfigureAwait(false);
    await scope.ServiceProvider.GetRequiredService<VumaRetailDbContext>().Database.MigrateAsync().ConfigureAwait(false);
    return;
}

app.UseVumaWeb();
app.UseVumaOpenApi();
app.MapVumaIdentity();
app.MapVumaCompanies();
app.MapVumaSync();
app.MapVumaDashboard();
app.MapVumaReporting();
app.MapVumaInventory();
app.MapVumaQuality();
app.MapVumaEcommerce();
app.MapVumaServiceManagement();
app.MapVumaLoyaltyPublic();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous()
    .WithTags("Infrastructure");

await app.RunAsync().ConfigureAwait(false);

/// <summary>Marks the cloud API assembly for tests and DI scanning.</summary>
public partial class Program;
