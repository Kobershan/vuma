using VumaRetail.ControlPlane;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ControlPlaneDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ControlPlane") ?? "Data Source=control-plane.db"));
builder.Services.AddScoped<ControlPlaneStore>();
builder.Services.AddHttpClient<ExternalLicenseSigner>();
builder.Services.AddSingleton<ILicenseSigner>(sp => sp.GetRequiredService<ExternalLicenseSigner>());
builder.Services.AddSingleton<IClock, SystemControlPlaneClock>();
builder.Services.AddSingleton<FleetOperations>();
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
device.MapPost("/activations/{id:guid}/rebind", async (Guid id, RebindRequest request, ControlPlaneStore store,
    ILicenseSigner signer, CancellationToken cancellationToken) =>
{
    if (id != request.RequestId) return Results.BadRequest("Route id and request id differ.");
    try { return Results.Ok(await store.RebindAsync(request, signer, cancellationToken)); }
    catch (KeyNotFoundException ex) { return Results.NotFound(ex.Message); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("signer", StringComparison.OrdinalIgnoreCase))
    { return Results.Problem("Licence issuance is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable); }
    catch (InvalidOperationException ex) { return Results.Conflict(ex.Message); }
});
device.MapDelete("/activations/{id:guid}", async (Guid id, string nodeId, ControlPlaneStore store,
    CancellationToken cancellationToken) =>
{
    await store.RevokeAsync(id, nodeId, cancellationToken);
    return Results.NoContent();
});
device.MapPost("/diagnostics", async (DiagnosticsRequest request, ControlPlaneStore store,
    CancellationToken cancellationToken) =>
{
    try { await store.RecordDiagnosticsAsync(request, cancellationToken); return Results.Accepted(); }
    catch (KeyNotFoundException ex) { return Results.NotFound(ex.Message); }
    catch (InvalidOperationException ex) { return Results.Conflict(ex.Message); }
});
device.MapPost("/telemetry/errors", async (TelemetryErrorRequest request, ControlPlaneStore store,
    CancellationToken cancellationToken) =>
{
    try { await store.RecordTelemetryErrorAsync(request, cancellationToken); return Results.Accepted(); }
    catch (KeyNotFoundException ex) { return Results.NotFound(ex.Message); }
    catch (InvalidOperationException ex) { return Results.Conflict(ex.Message); }
});
device.MapGet("/updates/check", (string nodeId, string version, string channel, FleetOperations fleet) =>
{
    if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(channel))
        return Results.BadRequest("nodeId and channel are required.");
    RolloutPlan? plan = fleet.SelectUpdate(nodeId, version ?? string.Empty, channel);
    return Results.Ok(new UpdateCheckResponse(nodeId, version ?? string.Empty, channel,
        plan?.Version, plan is not null));
});
app.Run();

public partial class Program;

file sealed class SystemControlPlaneClock : IClock
{
    public DateTimeOffset UtcNow => TimeProvider.System.GetUtcNow();
}
