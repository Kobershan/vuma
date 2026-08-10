using Microsoft.EntityFrameworkCore;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Security.Identity;
using VumaRetail.StoreServer;
using VumaRetail.Web;
using VumaRetail.Web.Api;
using VumaRetail.Web.Diagnostics;
using VumaRetail.Web.Identity;

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

// Order matters: AddVumaWeb registers the authenticated IPrincipalAccessor, and
// AddVumaPersistence only supplies its system fallback if nothing has claimed the slot.
builder.Services.AddVumaWeb(jwt, host);
builder.Services.AddVumaPersistence(connectionString);

WebApplication app = builder.Build();

if (args.Contains("--seed", StringComparer.Ordinal))
{
    await DemoSeed.RunAsync(app.Services).ConfigureAwait(false);
    return;
}

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    using IServiceScope scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<VumaRetailDbContext>().Database.MigrateAsync().ConfigureAwait(false);
    return;
}

app.UseVumaWeb();
app.UseVumaOpenApi();
app.MapVumaIdentity();

// Deliberately un-versioned, and on the closed list in VumaApi.UnversionedRoutes: a health probe is
// infrastructure, not API surface, and a load balancer should never have to be reconfigured because
// the business API moved to v2.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous()
    .WithTags("Infrastructure");

await app.RunAsync().ConfigureAwait(false);

/// <summary>Marks the store server assembly for tests and DI scanning.</summary>
public partial class Program;
