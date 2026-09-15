using VumaRetail.ControlPlane;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ControlPlaneStore>();
builder.Services.AddSingleton<ILicenseSigner, ExternalLicenseSigner>();
builder.Services.AddOpenApi();
WebApplication app = builder.Build();
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
device.MapPost("/metering", (MeteringRequest request, ControlPlaneStore store) =>
{
    store.AcceptMetering(request);
    return Results.Accepted();
});
app.Run();

public partial class Program;
