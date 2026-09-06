using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace VumaRetail.Web;

/// <summary>
/// API endpoints for group receipting across companies (Stage 07c).
/// </summary>
public static class GroupReceiptEndpoints
{
    public static IEndpointRouteBuilder MapGroupReceiptEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/group-receipts")
            .WithTags("Group Receipts")
            .RequireAuthorization();

        group.MapPost("/", async (
            CaptureGroupReceiptRequest request,
            CaptureGroupReceiptCommandHandler handler,
            CancellationToken ct) =>
        {
            var command = new CaptureGroupReceiptCommand
            {
                TenantId = request.TenantId,
                CapturingCompanyId = request.CapturingCompanyId,
                BankAccountId = request.BankAccountId,
                Amount = new Money(request.Amount, request.Currency),
                TenderType = request.TenderType,
                Reference = request.Reference,
                CapturedAt = request.CapturedAt,
            };

            Guid id = await handler.HandleAsync(command, ct);
            return Results.Created($"/api/v1/group-receipts/{id}", new { Id = id });
        })
        .Produces<Guid>(StatusCodes.Status201Created)
        .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
        .RequirePermission("registry.receipt.capture");

        group.MapPost("/{id:guid}/allocations", async (
            Guid id,
            AllocateGroupReceiptRequest request,
            AllocateGroupReceiptCommandHandler handler,
            CancellationToken ct) =>
        {
            var command = new AllocateGroupReceiptCommand
            {
                TenantId = request.TenantId,
                GroupReceiptId = id,
                CompanyId = request.CompanyId,
                CustomerPartnerId = request.CustomerPartnerId,
                Amount = new Money(request.Amount, request.Currency),
                TargetInvoiceIds = request.TargetInvoiceIds,
            };

            await handler.HandleAsync(command, ct);
            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
        .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
        .RequirePermission("registry.receipt.allocate");

        group.MapPost("/{id:guid}/reverse", async (
            Guid id,
            ReverseGroupReceiptRequest request,
            ReverseGroupReceiptCommandHandler handler,
            CancellationToken ct) =>
        {
            var command = new ReverseGroupReceiptCommand
            {
                TenantId = request.TenantId,
                GroupReceiptId = id,
            };

            await handler.HandleAsync(command, ct);
            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
        .RequirePermission("registry.receipt.reverse");

        group.MapGet("/unallocated", async (
            Guid tenantId,
            GetUnallocatedGroupReceiptsQueryHandler handler,
            CancellationToken ct) =>
        {
            var query = new GetUnallocatedGroupReceiptsQuery { TenantId = tenantId };
            var results = await handler.HandleAsync(query, ct);
            return Results.Ok(results.Select(r => new GroupReceiptResponse
            {
                Id = r.Id,
                Amount = r.Amount.Amount,
                Currency = r.Amount.Currency,
                TenderType = r.TenderType,
                Reference = r.Reference,
                CapturedAt = r.CapturedAt,
                Status = r.Status.ToString(),
                UnallocatedAmount = r.Amount.Amount - r.Allocations
                    .Where(a => a.LegState is not GroupReceiptAllocationLegState.Compensated)
                    .Sum(a => a.Amount.Amount),
            }));
        })
        .Produces<IReadOnlyList<GroupReceiptResponse>>()
        .RequirePermission("registry.receipt.capture");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IGroupReceiptRepository repository,
            CancellationToken ct) =>
        {
            var receipt = await repository.GetByIdAsync(id, ct);
            if (receipt is null) return Results.NotFound();
            return Results.Ok(new GroupReceiptDetailResponse
            {
                Id = receipt.Id,
                Amount = receipt.Amount.Amount,
                Currency = receipt.Amount.Currency,
                TenderType = receipt.TenderType,
                Reference = receipt.Reference,
                CapturedAt = receipt.CapturedAt,
                Status = receipt.Status.ToString(),
                Allocations = receipt.Allocations.Select(a => new AllocationResponse
                {
                    Id = a.Id,
                    CompanyId = a.CompanyId,
                    Amount = a.Amount.Amount,
                    Currency = a.Amount.Currency,
                    LegState = a.LegState.ToString(),
                    AppliedAt = a.AppliedAt,
                }).ToList(),
            });
        })
        .Produces<GroupReceiptDetailResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequirePermission("registry.receipt.capture");

        return group;
    }
}

// ========== Request/Response DTOs ==========

public sealed class CaptureGroupReceiptRequest
{
    public Guid TenantId { get; init; }
    public Guid CapturingCompanyId { get; init; }
    public Guid BankAccountId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "ZAR";
    public string TenderType { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public DateTimeOffset CapturedAt { get; init; }
}

public sealed class AllocateGroupReceiptRequest
{
    public Guid TenantId { get; init; }
    public Guid CompanyId { get; init; }
    public Guid? CustomerPartnerId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "ZAR";
    public IReadOnlyList<Guid>? TargetInvoiceIds { get; init; }
}

public sealed class ReverseGroupReceiptRequest
{
    public Guid TenantId { get; init; }
}

public sealed class GroupReceiptResponse
{
    public Guid Id { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string TenderType { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public DateTimeOffset CapturedAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal UnallocatedAmount { get; init; }
}

public sealed class GroupReceiptDetailResponse
{
    public Guid Id { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string TenderType { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public DateTimeOffset CapturedAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public IReadOnlyList<AllocationResponse> Allocations { get; init; } = [];
}

public sealed class AllocationResponse
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string LegState { get; init; } = string.Empty;
    public DateTimeOffset? AppliedAt { get; init; }
}
