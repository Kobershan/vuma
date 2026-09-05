using VumaRetail.Application.Abstractions.Registry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace VumaRetail.Web;

/// <summary>
/// API endpoints for consolidated reporting across companies (Stage 07c).
/// Every response carries the watermark: "Consolidated — management information, not a statutory statement" (ADR-106).
/// </summary>
public static class ConsolidationEndpoints
{
    public static IEndpointRouteBuilder MapConsolidationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reports/consolidated")
            .WithTags("Consolidated Reports")
            .RequireAuthorization();

        group.MapGet("/trial-balance", async (
            Guid tenantId,
            DateOnly asOf,
            IConsolidationService service,
            CancellationToken ct) =>
        {
            var result = await service.GetTrialBalanceAsync(tenantId, asOf, ct);
            return Results.Ok(new ConsolidatedTrialBalanceResponse
            {
                Watermark = result.Watermark,
                AsOf = result.AsOf,
                Accounts = result.Accounts.Select(a => new ConsolidatedAccountLineResponse
                {
                    AccountCode = a.AccountCode,
                    AccountName = a.AccountName,
                    AccountType = a.AccountType,
                    Debit = a.Debit.Amount,
                    Credit = a.Credit.Amount,
                    Currency = a.Debit.Currency,
                }).ToList(),
                Contributors = result.Contributors.Select(c => new ContributorResponse
                {
                    CompanyId = c.CompanyId,
                    CompanyName = c.CompanyName,
                    AsAt = c.AsAt,
                    IsStale = c.IsStale,
                    StaleReason = c.StaleReason,
                }).ToList(),
            });
        })
        .Produces<ConsolidatedTrialBalanceResponse>()
        .RequirePermission("group.report.consolidated");

        group.MapGet("/income-statement", async (
            Guid tenantId,
            DateOnly from,
            DateOnly to,
            IConsolidationService service,
            CancellationToken ct) =>
        {
            var result = await service.GetIncomeStatementAsync(tenantId, from, to, ct);
            return Results.Ok(new ConsolidatedIncomeStatementResponse
            {
                Watermark = result.Watermark,
                From = result.From,
                To = result.To,
                IncomeAccounts = result.IncomeAccounts.Select(a => new ConsolidatedAccountLineResponse
                {
                    AccountCode = a.AccountCode,
                    AccountName = a.AccountName,
                    AccountType = a.AccountType,
                    Debit = a.Debit.Amount,
                    Credit = a.Credit.Amount,
                    Currency = a.Debit.Currency,
                }).ToList(),
                ExpenseAccounts = result.ExpenseAccounts.Select(a => new ConsolidatedAccountLineResponse
                {
                    AccountCode = a.AccountCode,
                    AccountName = a.AccountName,
                    AccountType = a.AccountType,
                    Debit = a.Debit.Amount,
                    Credit = a.Credit.Amount,
                    Currency = a.Debit.Currency,
                }).ToList(),
                NetIncome = result.NetIncome.Amount,
                NetIncomeCurrency = result.NetIncome.Currency,
                Contributors = result.Contributors.Select(c => new ContributorResponse
                {
                    CompanyId = c.CompanyId,
                    CompanyName = c.CompanyName,
                    AsAt = c.AsAt,
                    IsStale = c.IsStale,
                    StaleReason = c.StaleReason,
                }).ToList(),
            });
        })
        .Produces<ConsolidatedIncomeStatementResponse>()
        .RequirePermission("group.report.consolidated");

        group.MapGet("/balance-sheet", async (
            Guid tenantId,
            DateOnly asOf,
            IConsolidationService service,
            CancellationToken ct) =>
        {
            var result = await service.GetBalanceSheetAsync(tenantId, asOf, ct);
            return Results.Ok(new ConsolidatedBalanceSheetResponse
            {
                Watermark = result.Watermark,
                AsOf = result.AsOf,
                AssetAccounts = result.AssetAccounts.Select(a => new ConsolidatedAccountLineResponse
                {
                    AccountCode = a.AccountCode,
                    AccountName = a.AccountName,
                    AccountType = a.AccountType,
                    Debit = a.Debit.Amount,
                    Credit = a.Credit.Amount,
                    Currency = a.Debit.Currency,
                }).ToList(),
                LiabilityAccounts = result.LiabilityAccounts.Select(a => new ConsolidatedAccountLineResponse
                {
                    AccountCode = a.AccountCode,
                    AccountName = a.AccountName,
                    AccountType = a.AccountType,
                    Debit = a.Debit.Amount,
                    Credit = a.Credit.Amount,
                    Currency = a.Debit.Currency,
                }).ToList(),
                EquityAccounts = result.EquityAccounts.Select(a => new ConsolidatedAccountLineResponse
                {
                    AccountCode = a.AccountCode,
                    AccountName = a.AccountName,
                    AccountType = a.AccountType,
                    Debit = a.Debit.Amount,
                    Credit = a.Credit.Amount,
                    Currency = a.Debit.Currency,
                }).ToList(),
                Contributors = result.Contributors.Select(c => new ContributorResponse
                {
                    CompanyId = c.CompanyId,
                    CompanyName = c.CompanyName,
                    AsAt = c.AsAt,
                    IsStale = c.IsStale,
                    StaleReason = c.StaleReason,
                }).ToList(),
            });
        })
        .Produces<ConsolidatedBalanceSheetResponse>()
        .RequirePermission("group.report.consolidated");

        group.MapGet("/unapplied-legs", async (
            Guid tenantId,
            IConsolidationService service,
            CancellationToken ct) =>
        {
            var legs = await service.GetUnappliedLegsAsync(tenantId, ct);
            return Results.Ok(legs);
        })
        .Produces<IReadOnlyList<UnappliedLegDto>>()
        .RequirePermission("group.report.consolidated");

        return group;
    }
}

// ========== Response DTOs ==========

public sealed class ConsolidatedTrialBalanceResponse
{
    public string Watermark { get; init; } = string.Empty;
    public DateOnly AsOf { get; init; }
    public IReadOnlyList<ConsolidatedAccountLineResponse> Accounts { get; init; } = [];
    public IReadOnlyList<ContributorResponse> Contributors { get; init; } = [];
}

public sealed class ConsolidatedIncomeStatementResponse
{
    public string Watermark { get; init; } = string.Empty;
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public IReadOnlyList<ConsolidatedAccountLineResponse> IncomeAccounts { get; init; } = [];
    public IReadOnlyList<ConsolidatedAccountLineResponse> ExpenseAccounts { get; init; } = [];
    public decimal NetIncome { get; init; }
    public string NetIncomeCurrency { get; init; } = string.Empty;
    public IReadOnlyList<ContributorResponse> Contributors { get; init; } = [];
}

public sealed class ConsolidatedBalanceSheetResponse
{
    public string Watermark { get; init; } = string.Empty;
    public DateOnly AsOf { get; init; }
    public IReadOnlyList<ConsolidatedAccountLineResponse> AssetAccounts { get; init; } = [];
    public IReadOnlyList<ConsolidatedAccountLineResponse> LiabilityAccounts { get; init; } = [];
    public IReadOnlyList<ConsolidatedAccountLineResponse> EquityAccounts { get; init; } = [];
    public IReadOnlyList<ContributorResponse> Contributors { get; init; } = [];
}

public sealed class ConsolidatedAccountLineResponse
{
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public string AccountType { get; init; } = string.Empty;
    public decimal Debit { get; init; }
    public decimal Credit { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed class ContributorResponse
{
    public Guid CompanyId { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public DateTimeOffset AsAt { get; init; }
    public bool IsStale { get; init; }
    public string? StaleReason { get; init; }
}
