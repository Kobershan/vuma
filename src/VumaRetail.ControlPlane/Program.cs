using VumaRetail.ControlPlane;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ControlPlaneDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ControlPlane") ?? "Data Source=control-plane.db"));
builder.Services.AddScoped<ControlPlaneStore>();
builder.Services.AddHttpClient<ExternalLicenseSigner>();
builder.Services.AddSingleton<ILicenseSigner>(sp => sp.GetRequiredService<ExternalLicenseSigner>());
builder.Services.AddOpenApi();
WebApplication app = builder.Build();
using (IServiceScope scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>().Database.EnsureCreatedAsync();
}
if (!app.Environment.IsDevelopment())
{
    if (!Uri.TryCreate(app.Configuration["ControlPlane:SignerEndpoint"], UriKind.Absolute, out Uri? signer)
        || signer.Scheme != Uri.UriSchemeHttps)
        throw new InvalidOperationException("Production control plane requires an HTTPS signer endpoint.");
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps || await context.Connection.GetClientCertificateAsync() is null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        await next();
    });
}
app.UseHttpsRedirection();
app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
RouteGroupBuilder device = app.MapGroup("/device/v1");
device.MapPost("/activations", async (ActivationRequest request, ControlPlaneStore store,
    ILicenseSigner signer, CancellationToken cancellationToken) =>
{
    try { return Results.Created("/device/v1/activations", await store.ActivateAsync(request, signer, cancellationToken)); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("signer", StringComparison.OrdinalIgnoreCase))
    { return Results.Problem("Licence issuance is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable); }
    catch (InvalidOperationException ex) { return Results.Conflict(ex.Message); }
});
device.MapPost("/heartbeat", async (HeartbeatRequest request, ControlPlaneStore store) =>
    Results.Ok(await store.HeartbeatAsync(request)));
device.MapPost("/lease", async (LeaseRequest request, ControlPlaneStore store, ILicenseSigner signer,
    CancellationToken cancellationToken) =>
    Results.Ok(await store.RefreshLeaseAsync(request, signer, cancellationToken)));
device.MapPost("/metering", async (MeteringRequest request, ControlPlaneStore store) =>
{
    await store.AcceptMeteringAsync(request);
    return Results.Accepted();
});
app.Run();

public partial class Program;
