using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.CustomerAccounts.Commands.Accounts;
using VumaRetail.Application.CustomerAccounts.Commands.LayBy;
using VumaRetail.Application.CustomerAccounts.Permissions;
using VumaRetail.Application.CustomerAccounts.Queries;
using VumaRetail.Contracts.CustomerAccounts;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.CustomerAccounts;

/// <summary>
/// The <c>customer-accounts</c> module's endpoints: credit accounts, lay-by agreements and their
/// statements.
/// </summary>
/// <remarks>
/// R3: nothing exists in a UI before it exists here. No <c>PUT</c> or <c>DELETE</c> anywhere —
/// accounts are held or closed, agreements are completed, cancelled or expired, and money moves
/// only through append-only rows (§7 rules 7–8).
/// </remarks>
public static class CustomerAccountsEndpoints
{
    /// <summary>Maps the customer-accounts endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaCustomerAccounts(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        RouteGroupBuilder accounts = api.MapGroup("/customer-accounts").WithTags("CustomerAccounts").RequireModule("customer-accounts");

        accounts.MapPost("/", OpenAccountAsync)
            .RequirePermission(CustomerAccountsPermissions.AccountManage)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens a customer credit account with an approved limit and terms.");

        accounts.MapGet("/{accountId:guid}", GetAccountAsync)
            .RequirePermission(CustomerAccountsPermissions.AccountView)
            .Produces<AccountResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One account with its holders.");

        accounts.MapPost("/{accountId:guid}/limit", SetLimitAsync)
            .RequirePermission(CustomerAccountsPermissions.AccountManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Sets a new credit limit. Approval-gated.");

        accounts.MapPost("/{accountId:guid}/hold", HoldAccountAsync)
            .RequirePermission(CustomerAccountsPermissions.AccountManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Freezes an account. Approval-gated.");

        accounts.MapPost("/{accountId:guid}/release", ReleaseAccountAsync)
            .RequirePermission(CustomerAccountsPermissions.AccountManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Unfreezes an account.");

        accounts.MapPost("/{accountId:guid}/payments", RecordPaymentAsync)
            .RequirePermission(CustomerAccountsPermissions.AccountManage)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Records a payment with optional invoice allocations.");

        accounts.MapPost("/{accountId:guid}/holders", AuthoriseHolderAsync)
            .RequirePermission(CustomerAccountsPermissions.AccountManage)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Authorises a buyer on a business account.");

        accounts.MapGet("/{accountId:guid}/statement", GetStatementAsync)
            .RequirePermission(CustomerAccountsPermissions.AccountView)
            .Produces<StatementResponse>()
            .WithSummary("Open items with a running owed balance.");

        accounts.MapGet("/{accountId:guid}/ageing", GetAgeingAsync)
            .RequirePermission(CustomerAccountsPermissions.AccountView)
            .Produces<AgeingResponse>()
            .WithSummary("Arrears in Current/30/60/90/120+ buckets.");

        accounts.MapGet("/{accountId:guid}/credit-check", CheckCreditAsync)
            .RequirePermission(CustomerAccountsPermissions.AccountView)
            .Produces<CreditCheckResponse>()
            .WithSummary("Tender-time limit answer, offline queue included.");

        RouteGroupBuilder layby = api.MapGroup("/layby").WithTags("CustomerAccounts").RequireModule("customer-accounts");

        layby.MapPost("/", OpenLayByAsync)
            .RequirePermission(CustomerAccountsPermissions.LayByManage)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens a lay-by: frozen price, payment plan, held stock.");

        layby.MapGet("/{agreementId:guid}", GetLayByAsync)
            .RequirePermission(CustomerAccountsPermissions.LayByManage)
            .Produces<LayByResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One agreement with its frozen lines and payments.");

        layby.MapPost("/{agreementId:guid}/instalments", RecordInstalmentAsync)
            .RequirePermission(CustomerAccountsPermissions.LayByManage)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Records an instalment with a running-balance receipt.");

        layby.MapPost("/{agreementId:guid}/complete", CompleteLayByAsync)
            .RequirePermission(CustomerAccountsPermissions.LayByManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Completes a fully paid agreement. Connectivity required.");

        layby.MapPost("/{agreementId:guid}/cancel", CancelLayByAsync)
            .RequirePermission(CustomerAccountsPermissions.LayByManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Cancels per the snapshotted terms: refund less fee.");

        return endpoints;
    }

    private static async Task<IResult> OpenAccountAsync(
        CreateAccountRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(
            new OpenCustomerAccountCommand(
                request.PartnerId, request.CreditLimitAmount, request.CreditLimitCurrency, request.TermsDays),
            cancellationToken);
        return Results.Created($"/api/v1/customer-accounts/{id}", id);
    }

    private static async Task<IResult> GetAccountAsync(
        Guid accountId, ICustomerAccountRepository accounts, IAccountHolderRepository holders,
        CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(accountId, cancellationToken);
        if (account is null)
        {
            return Results.NotFound();
        }

        var authorised = await holders.ListForAccountAsync(accountId, cancellationToken);
        return Results.Ok(new AccountResponse(
            account.Id, account.AccountNumber, account.PartnerId,
            account.CreditLimit.Amount, account.CreditLimit.Currency, account.TermsDays,
            account.Status.ToString(), account.HoldReason));
    }

    private static async Task<IResult> SetLimitAsync(
        Guid accountId, SetLimitRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(
            new SetCreditLimitCommand(accountId, request.Amount, request.Currency), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> HoldAccountAsync(
        Guid accountId, HoldRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new PlaceAccountHoldCommand(accountId, request.Reason), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ReleaseAccountAsync(
        Guid accountId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ReleaseAccountHoldCommand(accountId), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RecordPaymentAsync(
        Guid accountId, RecordPaymentRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(
            new RecordAccountPaymentCommand(
                accountId, request.Amount, request.Currency, request.Channel, request.ReceiptReference),
            cancellationToken);
        return Results.Created($"/api/v1/customer-accounts/{accountId}/payments/{id}", id);
    }

    private static async Task<IResult> AuthoriseHolderAsync(
        Guid accountId, AuthoriseHolderRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(
            new AuthoriseHolderCommand(
                accountId, request.UserId, request.DisplayName,
                request.ChargeLimitAmount, request.ChargeLimitCurrency),
            cancellationToken);
        return Results.Created($"/api/v1/customer-accounts/{accountId}/holders/{id}", id);
    }

    private static async Task<IResult> GetStatementAsync(
        Guid accountId, IDispatcher dispatcher, CancellationToken cancellationToken,
        DateOnly? from = null, DateOnly? to = null)
    {
        var lines = await dispatcher.QueryAsync(
            new GetAccountStatementQuery(
                accountId,
                from ?? new DateOnly(2000, 1, 1),
                to ?? DateOnly.FromDateTime(DateTime.UtcNow)),
            cancellationToken);
        return Results.Ok(new StatementResponse(accountId,
            [.. lines.Select(l => new StatementLineResponse(l.Date, l.Description, l.Debit.Amount, l.Credit.Amount, l.RunningBalance.Amount))]));
    }

    private static async Task<IResult> GetAgeingAsync(
        Guid accountId, IDispatcher dispatcher, CancellationToken cancellationToken, DateOnly? asAt = null)
    {
        var buckets = await dispatcher.QueryAsync(
            new GetAccountAgeingQuery(accountId, asAt ?? DateOnly.FromDateTime(DateTime.UtcNow)),
            cancellationToken);
        return Results.Ok(new AgeingResponse(accountId,
            [.. buckets.Select(b => new AgeingBucketResponse(b.Bucket, b.Balance.Amount))]));
    }

    private static async Task<IResult> CheckCreditAsync(
        Guid accountId, IDispatcher dispatcher, CancellationToken cancellationToken,
        decimal tenderAmount = 0m, string currency = "ZAR", decimal queuedOfflineTotal = 0m, Guid? holderUserId = null)
    {
        CreditCheckResult result = await dispatcher.QueryAsync(
            new CheckCreditLimitQuery(accountId, tenderAmount, currency, queuedOfflineTotal, holderUserId),
            cancellationToken);
        return Results.Ok(new CreditCheckResponse(result.Approved, result.Available.Amount, result.RefusalReason));
    }

    private static async Task<IResult> OpenLayByAsync(
        OpenLayByRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(
            new OpenLayByAgreementCommand(
                request.PartnerId, request.Currency,
                [.. request.Lines.Select(l => new LayByLineInput(l.ItemId, l.ItemVariantId, l.Quantity, l.Uom))],
                request.DepositAmount, request.DepositChannel, request.TermMonths, request.LocationCode),
            cancellationToken);
        return Results.Created($"/api/v1/layby/{id}", id);
    }

    private static async Task<IResult> GetLayByAsync(
        Guid agreementId, ILayByAgreementRepository laybys, CancellationToken cancellationToken)
    {
        var agreement = await laybys.FindAsync(agreementId, cancellationToken);
        if (agreement is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new LayByResponse(
            agreement.Id, agreement.AgreementNumber, agreement.Status.ToString(),
            agreement.AgreedTotal.Amount, agreement.PaidToDate.Amount, agreement.Remaining.Amount,
            agreement.AgreedTotal.Currency, agreement.ExpiryDate,
            [.. agreement.Lines.Select(l => new LayByLineResponse(
                l.Id, l.ItemId, l.ItemVariantId, l.QuantityValue, l.QuantityUom,
                l.AgreedUnitPrice.Amount, l.DiscountAmount.Amount, l.TaxAmount.Amount,
                l.Net.Amount, l.PackSizeDescription))],
            [.. agreement.Instalments.Select(i => new LayByInstalmentResponse(
                i.Sequence, i.Amount.Amount, i.ReceiptReference, i.PaidAt, i.Channel, i.TakenOffline))]));
    }

    private static async Task<IResult> RecordInstalmentAsync(
        Guid agreementId, RecordInstalmentRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(
            new RecordLayByInstalmentCommand(
                agreementId, request.Amount, request.Currency, request.Channel,
                request.ReceiptReference, request.TakenOffline),
            cancellationToken);
        return Results.Created($"/api/v1/layby/{agreementId}/instalments/{id}", id);
    }

    private static async Task<IResult> CompleteLayByAsync(
        Guid agreementId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new CompleteLayByAgreementCommand(agreementId), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> CancelLayByAsync(
        Guid agreementId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new CancelLayByAgreementCommand(agreementId), cancellationToken);
        return Results.NoContent();
    }
}
