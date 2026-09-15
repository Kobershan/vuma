using VumaRetail.ControlPlane;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ControlPlaneStore>();
builder.Services.AddHttpClient<ExternalLicenseSigner>();
builder.Services.AddSingleton<ILicenseSigner>(sp => sp.GetRequiredService<ExternalLicenseSigner>());
builder.Services.AddOpenApi();
WebApplication app = builder.Build();
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
device.MapPost("/heartbeat", (HeartbeatRequest request, ControlPlaneStore store) =>
    Results.Ok(store.Heartbeat(request)));
device.MapPost("/lease", async (LeaseRequest request, ControlPlaneStore store, ILicenseSigner signer,
    CancellationToken cancellationToken) =>
    Results.Ok(await store.RefreshLeaseAsync(request, signer, cancellationToken)));
device.MapPost("/metering", (MeteringRequest request, ControlPlaneStore store) =>
{
    store.AcceptMetering(request);
    return Results.Accepted();
});
app.Run();

public partial class Program;
