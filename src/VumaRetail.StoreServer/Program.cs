using Microsoft.EntityFrameworkCore;
using VumaRetail.Finance.Hosting;
using VumaRetail.Infrastructure.Backup;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Security.Identity;
using VumaRetail.Infrastructure.Sync;
using VumaRetail.Licensing;
using VumaRetail.Licensing.Hosting;
using VumaRetail.Licensing.Signing;
using VumaRetail.StoreServer;
using VumaRetail.Sync.Dispatch;
using VumaRetail.Web;
using VumaRetail.Web.Api;
using VumaRetail.Web.Catalog;
using VumaRetail.Web.Diagnostics;
using VumaRetail.Web.Finance;
using VumaRetail.Web.Identity;
using VumaRetail.Web.Inventory;
using VumaRetail.Web.Licensing;
using VumaRetail.Web.Partners;
using VumaRetail.Web.Pos;
using VumaRetail.Web.Sync;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddVumaLogging("VumaRetail.StoreServer");

JwtOptions jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
HostTenantOptions host = builder.Configuration.GetSection(HostTenantOptions.SectionName).Get<HostTenantOptions>()
    ?? new HostTenantOptions();

// A shipped default signing key is a master key for every installation that ever used it, so this is
// a refusal to start rather than a warning in a log nobody reads (docs/SECURITY.md §1).
if (jwt.UsesPlaceholderKey && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        $"{JwtOptions.SectionName}:SigningKey is still the development placeholder. Set a real signing "
        + "key before running outside Development.");
}

string connectionString = builder.Configuration.GetConnectionString("Vuma")
    ?? throw new InvalidOperationException("ConnectionStrings:Vuma is not configured.");

NodeIdentityOptions node = builder.Configuration.GetSection(NodeIdentityOptions.SectionName)
    .Get<NodeIdentityOptions>() ?? new NodeIdentityOptions();

SyncPeerOptions peer = builder.Configuration.GetSection(SyncPeerOptions.SectionName)
    .Get<SyncPeerOptions>() ?? new SyncPeerOptions();

OutboxDispatcherOptions dispatcher = builder.Configuration.GetSection(OutboxDispatcherOptions.SectionName)
    .Get<OutboxDispatcherOptions>() ?? new OutboxDispatcherOptions();

BackupVaultOptions vault = builder.Configuration.GetSection(BackupVaultOptions.SectionName)
    .Get<BackupVaultOptions>() ?? new BackupVaultOptions();

SnapshotEncryptionOptions encryption = builder.Configuration
    .GetSection(SnapshotEncryptionOptions.SectionName)
    .Get<SnapshotEncryptionOptions>() ?? new SnapshotEncryptionOptions();

PostgresBackupOptions postgres = builder.Configuration.GetSection(PostgresBackupOptions.SectionName)
    .Get<PostgresBackupOptions>() ?? new PostgresBackupOptions();

LicensingOptions licensing = builder.Configuration.GetSection(LicensingOptions.SectionName)
    .Get<LicensingOptions>() ?? new LicensingOptions();

// The same refusal the JWT signing key gets, for the same reason. The development licence key pair is
// derived from a seed compiled into the binaries, so anybody can mint a licence with it — which is
// exactly what makes it useful in Development and unacceptable anywhere else (ADR-050).
if (licensing.UsesDevelopmentKey && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        $"{LicensingOptions.SectionName}:PublicKey is still the development licence key. Pin the "
        + "production public key before running outside Development.");
}

// Beside the database rather than in it: the install id and the licence shadow copies have to survive
// a restore into a fresh database, because a rebuilt store is the same installation and telling the
// vendor otherwise on the first heartbeat after a disaster would look exactly like a clone.
string licensingState = builder.Configuration["Vuma:Licensing:StateDirectory"]
    ?? Path.Combine(AppContext.BaseDirectory, "licensing-state");

// Order matters: AddVumaWeb registers the authenticated IPrincipalAccessor, and
// AddVumaPersistence only supplies its system fallback if nothing has claimed the slot.
builder.Services.AddVumaWeb(jwt, host);
builder.Services.AddVumaPersistence(connectionString);

// Stage 06. Master data: items, variants, barcodes, units of measure, and suppliers/customers. Both
// self-register their permission declaration and core module manifest — see the remarks on
// AddVumaCatalog for why registration order relative to AddVumaIdentity/AddVumaLicensing does not matter.
builder.Services.AddVumaPlatform();
builder.Services.AddVumaCatalog();
builder.Services.AddVumaPartners();

// Stage 07. GL, AR, AP, banking, tax and the posting rules engine — CLAUDE.md §7 rule 12's mechanism.
// Built ahead of Inventory (08) and POS (09) because both post to it (ADR-016). The reconciliation
// host is registered separately: it needs the tenant this installation runs as, the same split
// AddVumaControlPlaneClient uses for LicensingHostTenant.
builder.Services.AddVumaFinance();
builder.Services.AddVumaFinanceReconciliation(new FinanceHostTenant(host.TenantId, host.StoreId));

// Stage 08. Locations, the append-only stock ledger, balances, transfers and stocktakes. Stock
// movements raise a valuation event that AddVumaFinance's posting rules engine turns into a journal;
// the binding is chosen when the publisher is resolved rather than when it is registered, so this
// line's position relative to AddVumaFinance does not matter.
builder.Services.AddVumaInventory();

// Stage 09. The till: sessions, sales, tenders, receipts and the cash-up. Depends on catalog (what is
// being sold), inventory (the stock it relieves) and finance (the tax it prices with and the journal a
// completed sale raises). Registered after all three; like AddVumaInventory, the financial binding is
// chosen when the publisher is resolved rather than when it is registered.
builder.Services.AddVumaPos();

// Stage 04. AddVumaSync goes after persistence: the outbox behaviour reads the DbContext's change
// tracker and the replication registry is built from its model.
builder.Services.AddVumaSync(node);
builder.Services.AddVumaBackup(connectionString, vault, encryption, postgres);
builder.Services.AddVumaOutboxDispatcher(
    peer,
    dispatcher,
    new HostTenantResolver(host.TenantId, host.StoreId));

// Stage 04b. After AddVumaSync: the activation repository resolves the current node, and the read-only
// guard goes into pipeline slot 50 — outside validation and outside the transaction, so a refused
// write costs no database round trip.
builder.Services.AddVumaLicensing(
    licensing,
    licensingState,
    builder.Configuration["Vuma:Licensing:IntegrityHash"] ?? string.Empty);

if (builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(licensing.ControlPlaneBaseAddress))
{
    // No vendor service on a developer's machine and none in CI. The in-process control plane signs
    // real documents with the development key, so the verification path under test is the production
    // one — ADR-022's shape, and the reason the whole enforcement ladder is exercisable at all.
    builder.Services.AddVumaInProcessControlPlane(LicenceSigner.Development);
}
else
{
    builder.Services.AddVumaControlPlaneClient(
        licensing,
        new LicensingHostTenant(host.TenantId, host.StoreId));
}

WebApplication app = builder.Build();

if (args.Contains("--seed", StringComparer.Ordinal))
{
    await DemoSeed.RunAsync(app.Services).ConfigureAwait(false);
    return;
}

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await BackupCli.MigrateAsync(app.Services).ConfigureAwait(false);
    return;
}

// Requirement R4's command line. A restore has to be runnable when the application is not — the
// machine it runs on has a fresh PostgreSQL and no back office to click in, because the back office
// is what is being restored.
if (args.Contains("--backup", StringComparer.Ordinal))
{
    Environment.ExitCode = await BackupCli.BackupAsync(app.Services, host.TenantId).ConfigureAwait(false);
    return;
}

if (args.Contains("--verify-backup", StringComparer.Ordinal))
{
    Environment.ExitCode = await BackupCli
        .VerifyAsync(app.Services, host.TenantId, ParseSnapshotId(args))
        .ConfigureAwait(false);
    return;
}

if (args.Contains("--restore", StringComparer.Ordinal))
{
    Environment.ExitCode = await BackupCli
        .RestoreAsync(
            app.Services,
            host.TenantId,
            ParseSnapshotId(args),
            BackupCli.ValueAfter(args, "--restore") ?? string.Empty)
        .ConfigureAwait(false);
    return;
}

app.UseVumaWeb();
app.UseVumaOpenApi();
app.MapVumaIdentity();
app.MapVumaSync();
app.MapVumaLicensing();
app.MapVumaCatalog();
app.MapVumaPartners();
app.MapVumaFinance();
app.MapVumaInventory();
app.MapVumaPos();

// Deliberately un-versioned, and on the closed list in VumaApi.UnversionedRoutes: a health probe is
// infrastructure, not API surface, and a load balancer should never have to be reconfigured because
// the business API moved to v2.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous()
    .WithTags("Infrastructure");

await app.RunAsync().ConfigureAwait(false);

/// <summary>Marks the store server assembly for tests and DI scanning.</summary>
public partial class Program
{
    /// <summary>
    /// Reads <c>--snapshot {id}</c>, or <see cref="Guid.Empty"/> to mean "the latest completed one".
    /// </summary>
    /// <param name="args">The command line.</param>
    private static Guid ParseSnapshotId(string[] args)
        => Guid.TryParse(BackupCli.ValueAfter(args, "--snapshot"), out Guid snapshotId)
            ? snapshotId
            : Guid.Empty;
}
