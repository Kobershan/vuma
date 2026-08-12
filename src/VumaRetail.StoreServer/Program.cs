using Microsoft.EntityFrameworkCore;
using VumaRetail.Infrastructure.Backup;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Security.Identity;
using VumaRetail.Infrastructure.Sync;
using VumaRetail.Licensing;
using VumaRetail.Licensing.Hosting;
using VumaRetail.Licensing.Signing;
using VumaRetail.Infrastructure.Workflow;
using VumaRetail.StoreServer;
using VumaRetail.Sync.Dispatch;
using VumaRetail.Web;
using VumaRetail.Web.Api;
using VumaRetail.Web.Diagnostics;
using VumaRetail.Web.Identity;
using VumaRetail.Web.Licensing;
using VumaRetail.Web.Sync;
using VumaRetail.Web.Workflow;

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

// Stage 05. After AddVumaLicensing/AddVumaWeb: ApprovalEngine reads Stage 02's IRoleRepository to
// check a decider's own permissions, and none of workflow's writes claim the ADR-028 payment
// exemption — a lapsed tenant should not be approving purchase orders any more than raising them.
DocumentBlobStoreOptions documentStore = builder.Configuration
    .GetSection(DocumentBlobStoreOptions.SectionName)
    .Get<DocumentBlobStoreOptions>() ?? new DocumentBlobStoreOptions();

if (string.IsNullOrWhiteSpace(documentStore.Directory))
{
    documentStore.Directory = Path.Combine(AppContext.BaseDirectory, "workflow-documents");
}

builder.Services.AddVumaWorkflow(documentStore);

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
app.MapVumaWorkflow();

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
