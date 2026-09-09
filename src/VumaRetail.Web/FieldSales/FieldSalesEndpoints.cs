using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.FieldSales.Commands;
using VumaRetail.Application.FieldSales.Permissions;
using VumaRetail.Application.FieldSales.Queries;
using VumaRetail.Contracts.FieldSales;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.FieldSales;

/// <summary>
/// Field sales: rep pro formas, approvals, availability and performance (Stage 14b). Every write
/// goes through a command handler; approval runs the saga through Stage 05's engine.
/// </summary>
public static class FieldSalesEndpoints
{
    /// <summary>Maps the field-sales endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapFieldSales(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        RouteGroupBuilder sales = api.MapGroup("/field-sales").WithTags("FieldSales").RequireModule("field-sales");

        sales.MapPost("/pro-formas", CaptureProFormaAsync)
            .RequirePermission(FieldSalesPermissions.ProFormaCapture)
            .Produces<FieldSalesIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Captures a pro forma order. Posts nothing, reserves nothing.")
            .WithDescription(
                "Replaying an idempotency key returns the existing pro forma rather than capturing "
                + "a second one, so a rep reconnecting after a drop replays safely.");

        sales.MapPost("/pro-formas/{proFormaId:guid}/submit", SubmitProFormaAsync)
            .RequirePermission(FieldSalesPermissions.ProFormaSubmit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Submits a draft for approval, raising the Stage 05 request.");

        sales.MapPost("/pro-formas/{proFormaId:guid}/amend", AmendProFormaAsync)
            .RequirePermission(FieldSalesPermissions.ProFormaSubmit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Returns a submitted pro forma to draft for amendment, with the reason.");

        sales.MapPost("/pro-formas/{proFormaId:guid}/withdraw", WithdrawProFormaAsync)
            .RequirePermission(FieldSalesPermissions.ProFormaSubmit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Withdraws a pro forma before a decision.");

        sales.MapGet("/pro-formas/{proFormaId:guid}", GetProFormaAsync)
            .RequirePermission(FieldSalesPermissions.ProFormaView)
            .Produces<ProFormaResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("One pro forma with its snapshots. Territory-enforced.");

        sales.MapPost("/pro-forma-credit-notes", CaptureCreditNoteAsync)
            .RequirePermission(FieldSalesPermissions.ProFormaCapture)
            .Produces<FieldSalesIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Captures a pro forma credit note against an invoice.");

        sales.MapPost("/pro-forma-credit-notes/{creditNoteId:guid}/submit", SubmitCreditNoteAsync)
            .RequirePermission(FieldSalesPermissions.ProFormaSubmit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Submits a credit proposal for approval.");

        sales.MapPost("/approvals/pro-formas/{proFormaId:guid}/approve", ApproveProFormaAsync)
            .RequirePermission(FieldSalesPermissions.ProFormaApprove)
            .Produces<ApproveProFormaResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Approves a pro forma: decides in Stage 05, then converts via the saga.")
            .WithDescription(
                "Re-prices against today's list (the delta is reported), holds group credit, "
                + "reserves per company, creates the order and issues the invoices.");

        sales.MapPost("/approvals/pro-formas/{proFormaId:guid}/reject", RejectProFormaAsync)
            .RequirePermission(FieldSalesPermissions.ProFormaApprove)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Rejects a pro forma with a reason. Reserves and posts nothing.");

        sales.MapPost("/approvals/credit-notes/{creditNoteId:guid}/approve", ApproveCreditNoteAsync)
            .RequirePermission(FieldSalesPermissions.ProFormaApprove)
            .Produces<FieldSalesIdResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Approves a credit proposal into a Stage 10 sales return.");

        sales.MapPost("/approvals/credit-notes/{creditNoteId:guid}/reject", RejectCreditNoteAsync)
            .RequirePermission(FieldSalesPermissions.ProFormaApprove)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Rejects a credit proposal with a reason.");

        sales.MapGet("/availability", GetAvailabilityAsync)
            .RequirePermission(FieldSalesPermissions.ProFormaView)
            .Produces<RepAvailabilityResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Group-wide available for a rep, per company, as-at stamped.");

        sales.MapGet("/performance", GetPerformanceAsync)
            .RequirePermission(FieldSalesPermissions.PerformanceOwn)
            .Produces<RepPerformanceResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("A closed period with its comparison and variance.");

        return endpoints;
    }

    /// <summary>
    /// Binds the acting company for this request scope when the caller named one explicitly.
    /// </summary>
    /// <remarks>
    /// The interim selection mechanism until per-request company middleware lands (the Stage 06c
    /// follow-up, same shape as sales/inventory endpoints): pro formas live in company databases
    /// (ADR-148), so every request names its company or inherits an already-bound scope.
    /// </remarks>
    private static void BindCompany(ICompanyContext company, Guid? companyId)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (companyId is null || companyId == Guid.Empty)
        {
            return;
        }

        if (company.CompanyId is { } bound && bound != companyId)
        {
            throw new ValidationFailedException(
                nameof(companyId),
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [nameof(companyId)] = ["The request names a different company than the scope already holds."],
                });
        }

        if (company.CompanyId is null)
        {
            company.SetCompany(companyId.Value);
        }
    }

    private static async Task<IResult> CaptureProFormaAsync(
        CaptureProFormaRequest request, IDispatcher dispatcher, ICompanyContext company, CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);
        Guid id = await dispatcher
            .SendAsync(
                new CaptureProFormaCommand(
                    request.RepId, request.CompanyId, request.PartnerId, request.Currency,
                    request.IdempotencyKey,
                    [.. request.Lines.Select(line => new ProFormaLineInput(
                        line.ItemId, line.ItemVariantId, line.QuantityValue, line.QuantityUom,
                        line.UnitPriceAmount, line.DiscountAmount, line.TaxCode, line.Currency))],
                    request.DeliveryLine1, request.DeliveryLine2, request.DeliveryCity,
                    request.DeliveryRegion, request.DeliveryPostalCode, request.DeliveryCountryCode),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/field-sales/pro-formas/{id}", new FieldSalesIdResponse(id));
    }

    private static async Task<IResult> SubmitProFormaAsync(
        Guid proFormaId, Guid? companyId, IDispatcher dispatcher, ICompanyContext company, CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);
        await dispatcher
            .SendAsync(new SubmitProFormaCommand(proFormaId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> AmendProFormaAsync(
        Guid proFormaId, AmendProFormaRequest request, Guid? companyId, IDispatcher dispatcher, ICompanyContext company, CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);
        await dispatcher
            .SendAsync(new AmendProFormaCommand(proFormaId, request.Reason), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> WithdrawProFormaAsync(
        Guid proFormaId, WithdrawProFormaRequest request, Guid? companyId, IDispatcher dispatcher, ICompanyContext company, CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);
        await dispatcher
            .SendAsync(new WithdrawProFormaCommand(proFormaId, request.Reason), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetProFormaAsync(
        Guid proFormaId, Guid callerRepId, Guid? companyId, IDispatcher dispatcher, ICompanyContext company, CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);
        ProFormaView view = await dispatcher
            .QueryAsync(new GetProFormaQuery(proFormaId, callerRepId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new ProFormaResponse(
            view.ProFormaId, view.ProFormaNumber, view.RepId, view.CompanyId, view.PartnerId,
            view.Currency, view.Status.ToString(), view.Gross.Amount,
            view.RepriceDelta?.Amount, view.ApprovalRequestId, view.ConvertedOrderId,
            view.DecisionReason, view.ExpiresAt,
            [.. view.Lines.Select(line => new ProFormaLineResponse(
                line.LineId, line.ItemId, line.ItemVariantId, line.QuantityValue, line.QuantityUom,
                line.UnitPrice.Amount, line.Discount.Amount, line.TaxCode, line.Tax.Amount,
                line.Net.Amount, line.Gross.Amount, line.PackSize,
                line.AvailableAtCapture.Amount, line.AvailabilityAsAt))]));
    }

    private static async Task<IResult> CaptureCreditNoteAsync(
        CaptureCreditNoteRequest request, IDispatcher dispatcher, ICompanyContext company, CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);
        Guid id = await dispatcher
            .SendAsync(
                new CaptureProFormaCreditNoteCommand(
                    request.RepId, request.CompanyId, request.OriginalInvoiceId,
                    request.OriginalInvoiceNumber, request.ReasonCode, request.Reason,
                    request.Currency, request.IdempotencyKey,
                    [.. request.Lines.Select(line => new ProFormaCreditLineInput(
                        line.OriginalInvoiceLineId, line.ItemId, line.ItemVariantId,
                        line.QuantityValue, line.QuantityUom, line.UnitPriceAmount,
                        line.TaxAmount, line.NetAmount, line.Currency))]),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/field-sales/pro-forma-credit-notes/{id}", new FieldSalesIdResponse(id));
    }

    private static async Task<IResult> SubmitCreditNoteAsync(
        Guid creditNoteId, Guid? companyId, IDispatcher dispatcher, ICompanyContext company, CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);
        await dispatcher
            .SendAsync(new SubmitProFormaCreditNoteCommand(creditNoteId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ApproveProFormaAsync(
        Guid proFormaId, ApproveProFormaRequest request, Guid? companyId, IDispatcher dispatcher, ICompanyContext company, CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);
        Guid orderId = await dispatcher
            .SendAsync(new ApproveProFormaCommand(proFormaId, request.Comment ?? string.Empty), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new ApproveProFormaResponse(orderId));
    }

    private static async Task<IResult> RejectProFormaAsync(
        Guid proFormaId, RejectProFormaRequest request, Guid? companyId, IDispatcher dispatcher, ICompanyContext company, CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);
        await dispatcher
            .SendAsync(new RejectProFormaCommand(proFormaId, request.Reason), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ApproveCreditNoteAsync(
        Guid creditNoteId, ApproveProFormaRequest request, Guid? companyId, IDispatcher dispatcher, ICompanyContext company, CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);
        Guid returnId = await dispatcher
            .SendAsync(new ApproveProFormaCreditNoteCommand(creditNoteId, request.Comment ?? string.Empty), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new FieldSalesIdResponse(returnId));
    }

    private static async Task<IResult> RejectCreditNoteAsync(
        Guid creditNoteId, RejectProFormaRequest request, Guid? companyId, IDispatcher dispatcher, ICompanyContext company, CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);
        await dispatcher
            .SendAsync(new RejectProFormaCreditNoteCommand(creditNoteId, request.Reason), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetAvailabilityAsync(
        Guid repId, Guid? itemId, Guid? itemVariantId, Guid? companyId,
        IDispatcher dispatcher, ICompanyContext company, CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);
        RepAvailabilityView view = await dispatcher
            .QueryAsync(new GetRepAvailabilityQuery(repId, itemId, itemVariantId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new RepAvailabilityResponse(
            view.ItemId, view.ItemVariantId, view.HasStaleContributor,
            [.. view.Companies.Select(row => new RepAvailabilityRowResponse(
                row.CompanyId, row.Available, row.AsAt))]));
    }

    private static async Task<IResult> GetPerformanceAsync(
        Guid repId, Guid? companyId, DateOnly period, DateOnly compareTo,
        Guid callerRepId, bool callerCanViewTeam,
        IDispatcher dispatcher, ICompanyContext scope, CancellationToken cancellationToken)
    {
        BindCompany(scope, companyId);
        RepPerformanceView view = await dispatcher
            .QueryAsync(new GetRepPerformanceQuery(
                repId, companyId, period, compareTo, callerRepId, callerCanViewTeam), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new RepPerformanceResponse(
            view.RepId, view.CompanyId, view.Period, view.CompareTo,
            view.NetValue, view.CompareNetValue, view.Variance, view.VariancePercent,
            view.Version, view.Reason));
    }
}
