#pragma warning disable CS1591
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Quality;
using VumaRetail.Domain.Quality;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Quality;

public static class QualityEndpoints
{
    public static IEndpointRouteBuilder MapVumaQuality(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder certificates = endpoints.MapVumaApi().MapGroup("/quality/certificates")
            .WithTags("Quality").RequireModule("quality");
        certificates.MapPost("/", IssueCertificateAsync).RequirePermission(QualityPermissions.Manage)
            .Produces<Guid>(StatusCodes.Status201Created);
        certificates.MapPost("/{id:guid}/revoke", RevokeCertificateAsync).RequirePermission(QualityPermissions.Manage)
            .Produces(StatusCodes.Status204NoContent);
        RouteGroupBuilder plans = endpoints.MapVumaApi().MapGroup("/quality/inspection-plans")
            .WithTags("Quality").RequireModule("quality");
        plans.MapPost("/", CreatePlanAsync).RequirePermission(QualityPermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        plans.MapPost("/{id:guid}/publish", PublishPlanAsync).RequirePermission(QualityPermissions.Manage)
            .Produces(StatusCodes.Status204NoContent);
        RouteGroupBuilder group = endpoints.MapVumaApi().MapGroup("/quality/holds")
            .WithTags("Quality").RequireModule("quality");
        group.MapPost("/", PlaceAsync).RequirePermission(QualityPermissions.Manage)
            .Produces<Guid>(StatusCodes.Status201Created);
        endpoints.MapVumaApi().MapGroup("/quality/inspections").WithTags("Quality").RequireModule("quality")
            .MapPost("/", RecordInspectionAsync).RequirePermission(QualityPermissions.Manage)
            .Produces<Guid>(StatusCodes.Status201Created);
        endpoints.MapVumaApi().MapGroup("/quality/non-conformances").WithTags("Quality").RequireModule("quality")
            .MapPost("/", OpenNonConformanceAsync).RequirePermission(QualityPermissions.Manage)
            .Produces<Guid>(StatusCodes.Status201Created);
        RouteGroupBuilder nonConformances = endpoints.MapVumaApi().MapGroup("/quality/non-conformances")
            .WithTags("Quality").RequireModule("quality");
        nonConformances.MapPost("/{id:guid}/corrective-action", StartCorrectiveActionAsync)
            .RequirePermission(QualityPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        nonConformances.MapPost("/{id:guid}/close", CloseNonConformanceAsync)
            .RequirePermission(QualityPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        group.MapPost("/{id:guid}/release", ReleaseAsync).RequirePermission(QualityPermissions.Manage)
            .Produces(StatusCodes.Status204NoContent);
        group.MapPost("/{id:guid}/reject", RejectAsync).RequirePermission(QualityPermissions.Manage)
            .Produces(StatusCodes.Status204NoContent);
        return endpoints;
    }

    private static async Task<IResult> PlaceAsync(PlaceQualityHoldRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(new PlaceQualityHoldCommand(request.OperationId, request.CompanyId, request.LocationId,
            request.ItemId, request.ItemVariantId, request.Quantity, request.UnitOfMeasure, request.Reason), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/quality/holds/{id:D}", id);
    }

    private static async Task<IResult> ReleaseAsync(Guid id, ReleaseQualityHoldRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ReleaseQualityHoldCommand(id, request.Reason), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> RecordInspectionAsync(RecordInspectionRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(new RecordInspectionCommand(request.OperationId, request.CompanyId, request.HoldId, request.PlanId,
            request.Passed, request.SampleSize, request.Evidence), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/quality/inspections/{id:D}", id);
    }

    private static async Task<IResult> CreatePlanAsync(CreateInspectionPlanRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(new CreateInspectionPlanCommand(request.CompanyId, request.ItemId, request.ItemVariantId,
            request.Version, request.Name, request.SampleSize, request.AcceptanceCriteria), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/quality/inspection-plans/{id:D}", id);
    }

    private static async Task<IResult> IssueCertificateAsync(IssueQualityCertificateRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(new IssueQualityCertificateCommand(request.CompanyId, request.ItemId, request.ItemVariantId,
            request.CertificateNumber, request.Issuer, request.IssuedAt, request.ExpiresAt, request.Evidence), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/quality/certificates/{id:D}", id);
    }

    private static async Task<IResult> RevokeCertificateAsync(Guid id, RevokeQualityCertificateRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new RevokeQualityCertificateCommand(id, request.Reason), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> PublishPlanAsync(Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new PublishInspectionPlanCommand(id), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> RejectAsync(Guid id, ReleaseQualityHoldRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new RejectQualityHoldCommand(id, request.Reason), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> OpenNonConformanceAsync(OpenNonConformanceRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(new OpenNonConformanceCommand(request.OperationId, request.CompanyId, request.HoldId,
            request.Severity, request.Description), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/quality/non-conformances/{id:D}", id);
    }

    private static async Task<IResult> StartCorrectiveActionAsync(Guid id, CorrectiveActionRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new StartCorrectiveActionCommand(id, request.OperationId), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> CloseNonConformanceAsync(Guid id, CloseNonConformanceRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new CloseNonConformanceCommand(id, request.OperationId, request.Resolution), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    public sealed record PlaceQualityHoldRequest(Guid OperationId, Guid CompanyId, Guid LocationId, Guid? ItemId, Guid? ItemVariantId, decimal Quantity, string UnitOfMeasure, string Reason);
    public sealed record ReleaseQualityHoldRequest(string Reason);
    public sealed record RecordInspectionRequest(Guid OperationId, Guid CompanyId, Guid HoldId, Guid? PlanId, bool Passed, int SampleSize, string Evidence);
    public sealed record CreateInspectionPlanRequest(Guid CompanyId, Guid? ItemId, Guid? ItemVariantId, int Version, string Name, int SampleSize, string AcceptanceCriteria);
    public sealed record IssueQualityCertificateRequest(Guid CompanyId, Guid? ItemId, Guid? ItemVariantId, string CertificateNumber, string Issuer, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, string Evidence);
    public sealed record RevokeQualityCertificateRequest(string Reason);
    public sealed record OpenNonConformanceRequest(Guid OperationId, Guid CompanyId, Guid HoldId, NonConformanceSeverity Severity, string Description);
    public sealed record CorrectiveActionRequest(Guid OperationId);
    public sealed record CloseNonConformanceRequest(Guid OperationId, string Resolution);
}
