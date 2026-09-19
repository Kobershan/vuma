using Microsoft.EntityFrameworkCore;
using VumaRetail.ControlPlane;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ControlPlaneDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ControlPlane") ?? "Data Source=control-plane.db"));
builder.Services.AddScoped<ControlPlaneStore>();
builder.Services.AddHttpClient<ExternalLicenseSigner>();
builder.Services.AddSingleton<ILicenseSigner>(sp => sp.GetRequiredService<ExternalLicenseSigner>());
builder.Services.AddSingleton<IClock, SystemControlPlaneClock>();
builder.Services.AddSingleton<FleetOperations>();
builder.Services.AddSingleton<AbuseDetector>();
builder.Services.AddSingleton<VendorProvisioning>();
builder.Services.AddSingleton<UsageRollupAggregator>();
builder.Services.AddSingleton<DunningTracker>();
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
    {
        throw new InvalidOperationException("Production control plane requires an HTTPS signer endpoint.");
    }

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
    if (id != request.RequestId)
    {
        return Results.BadRequest("Route id and request id differ.");
    }

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
    {
        return Results.BadRequest("nodeId and channel are required.");
    }

    RolloutPlan? plan = fleet.SelectUpdate(nodeId, version ?? string.Empty, channel);
    return Results.Ok(new UpdateCheckResponse(nodeId, version ?? string.Empty, channel,
        plan?.Version, plan is not null));
});

RouteGroupBuilder vendor = app.MapGroup("/vendor/v1");
vendor.MapGet("/fleet/health", (HttpContext http, FleetOperations fleet) =>
{
    if (!VendorApiAuthorization.IsAllowed(http, VendorRole.Support))
    {
        return Results.Forbid();
    }

    return Results.Ok(fleet.Health(TimeSpan.FromHours(24)));
});
vendor.MapPost("/rollouts", (StartRolloutRequest request, HttpContext http, FleetOperations fleet) =>
{
    if (!VendorApiAuthorization.IsAllowed(http, VendorRole.Engineering))
    {
        return Results.Forbid();
    }

    return Results.Ok(fleet.StartRollout(request.Version, request.Channel, request.Percentage));
});
vendor.MapPost("/rollouts/{id:guid}/halt", (Guid id, HttpContext http, FleetOperations fleet) =>
{
    if (!VendorApiAuthorization.IsAllowed(http, VendorRole.Engineering))
    {
        return Results.Forbid();
    }

    return Results.Ok(fleet.HaltRollout(id));
});
vendor.MapGet("/abuse", (HttpContext http, AbuseDetector abuse) =>
{
    if (!VendorApiAuthorization.IsAllowed(http, VendorRole.Support))
    {
        return Results.Forbid();
    }

    return Results.Ok(abuse.Cases);
});
vendor.MapPost("/abuse/observations", (ObserveAbuseRequest request, HttpContext http, AbuseDetector abuse) =>
{
    if (!VendorApiAuthorization.IsAllowed(http, VendorRole.Support))
    {
        return Results.Forbid();
    }

    return Results.Ok(abuse.Observe(new DeviceObservation(request.LicenseKey, request.InstallFingerprint,
        request.NodeId, request.MonotonicCounter, request.At, request.Latitude, request.Longitude, request.DocumentSeries)));
});
vendor.MapPost("/tenants", (ProvisionTenantRequest request, HttpContext http, VendorProvisioning provisioning) =>
{
    if (!VendorApiAuthorization.IsAllowed(http, VendorRole.Admin))
    {
        return Results.Forbid();
    }

    if (request.TrialDays is <= 0 or > 365)
    {
        return Results.BadRequest("trialDays must be between 1 and 365.");
    }

    return Results.Created($"/vendor/v1/tenants/{request.TenantId}", provisioning.Provision(request.TenantId, TimeSpan.FromDays(request.TrialDays)));
});
vendor.MapPost("/support-grants", (SupportGrantRequest request, HttpContext http, IClock clock) =>
{
    if (!VendorApiAuthorization.IsAllowed(http, VendorRole.Support))
    {
        return Results.Forbid();
    }

    return Results.Ok(SupportGrantPolicy.Create(Guid.NewGuid(), request.TenantId, request.RequestedBy,
        request.ExpiresAt, request.TenantApproved, clock.UtcNow));
});
vendor.MapPost("/metering", (UsageRollup rollup, HttpContext http, UsageRollupAggregator aggregator) =>
{
    if (!VendorApiAuthorization.IsAllowed(http, VendorRole.Billing))
    {
        return Results.Forbid();
    }

    return Results.Ok(new { accepted = aggregator.Add(rollup) });
});
vendor.MapGet("/tenants/{tenantId}/usage", (string tenantId, DateOnly from, DateOnly through,
    HttpContext http, UsageRollupAggregator aggregator) =>
{
    if (!VendorApiAuthorization.IsAllowed(http, VendorRole.Billing))
    {
        return Results.Forbid();
    }

    return Results.Ok(aggregator.ForTenant(tenantId, from, through));
});
vendor.MapPost("/billing/calculate", (BillingCalculationRequest request, HttpContext http) =>
{
    if (!VendorApiAuthorization.IsAllowed(http, VendorRole.Billing))
    {
        return Results.Forbid();
    }

    return Results.Ok(BillingCalculator.Calculate(request.Plan, request.Usage));
});
app.Run();

public partial class Program;

file sealed class SystemControlPlaneClock : IClock
{
    public DateTimeOffset UtcNow => TimeProvider.System.GetUtcNow();
}
