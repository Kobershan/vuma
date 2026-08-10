using Microsoft.EntityFrameworkCore;
using ZenithRetail.Infrastructure.DependencyInjection;
using ZenithRetail.Infrastructure.Persistence;
using ZenithRetail.Infrastructure.Security.Identity;
using ZenithRetail.StoreServer;
using ZenithRetail.Web;
using ZenithRetail.Web.Identity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

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

string connectionString = builder.Configuration.GetConnectionString("Zenith")
    ?? throw new InvalidOperationException("ConnectionStrings:Zenith is not configured.");

// Order matters: AddZenithWeb registers the authenticated IPrincipalAccessor, and
// AddZenithPersistence only supplies its system fallback if nothing has claimed the slot.
builder.Services.AddZenithWeb(jwt, host);
builder.Services.AddZenithPersistence(connectionString);

WebApplication app = builder.Build();

if (args.Contains("--seed", StringComparer.Ordinal))
{
    await DemoSeed.RunAsync(app.Services).ConfigureAwait(false);
    return;
}

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    using IServiceScope scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ZenithRetailDbContext>().Database.MigrateAsync().ConfigureAwait(false);
    return;
}

app.UseZenithWeb();
app.MapZenithIdentity();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

await app.RunAsync().ConfigureAwait(false);

/// <summary>Marks the store server assembly for tests and DI scanning.</summary>
public partial class Program;
