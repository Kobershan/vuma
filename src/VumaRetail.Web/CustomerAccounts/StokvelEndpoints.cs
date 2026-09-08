using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.CustomerAccounts.Commands.Stokvels;
using VumaRetail.Application.CustomerAccounts.Permissions;
using VumaRetail.Application.CustomerAccounts.Queries;
using VumaRetail.Contracts.CustomerAccounts;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Primitives;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.CustomerAccounts;

/// <summary>
/// The stokvel half of the <c>customer-accounts</c> module: groups, members, contributions,
/// benefits, hampers, payouts and statements.
/// </summary>
/// <remarks>
/// R3: nothing exists in a UI before it exists here. No <c>PUT</c> or <c>DELETE</c> anywhere —
/// members leave rather than delete, payouts settle rather than edit, and money moves only
/// through append-only rows (§7 rules 7–8).
/// </remarks>
public static class StokvelEndpoints
{
    /// <summary>Maps the stokvel endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaStokvels(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        RouteGroupBuilder stokvels = api.MapGroup("/stokvels").WithTags("CustomerAccounts").RequireModule("customeraccounts");

        stokvels.MapPost("/", CreateGroupAsync)
            .RequirePermission(CustomerAccountsPermissions.StokvelManage)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens a stokvel group.");

        stokvels.MapGet("/{groupId:guid}", GetGroupAsync)
            .RequirePermission(CustomerAccountsPermissions.StokvelView)
            .Produces<StokvelResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One group.");

        stokvels.MapPost("/{groupId:guid}/members", AddMemberAsync)
            .RequirePermission(CustomerAccountsPermissions.StokvelManage)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Joins a member to a group.");

        stokvels.MapPost("/{groupId:guid}/contributions", RecordContributionAsync)
            .RequirePermission(CustomerAccountsPermissions.StokvelManage)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Records a contribution with a running-balance receipt.");

        stokvels.MapPost("/{groupId:guid}/allocate-benefits", AllocateBenefitsAsync)
            .RequirePermission(CustomerAccountsPermissions.StokvelManage)
            .Produces<IReadOnlyList<Guid>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Allocates a bonus pool time-weighted across the group's members.");

        stokvels.MapPost("/{groupId:guid}/payouts", RequestPayoutAsync)
            .RequirePermission(CustomerAccountsPermissions.StokvelManage)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Requests a payout against a member's available balance.");

        stokvels.MapPost("/payouts/{payoutId:guid}/approve", ApprovePayoutAsync)
            .RequirePermission(CustomerAccountsPermissions.StokvelManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Approves a payout. Approval-gated.");

        stokvels.MapPost("/payouts/{payoutId:guid}/settle", SettlePayoutAsync)
            .RequirePermission(CustomerAccountsPermissions.StokvelManage)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Settles an approved payout: goods/hamper as a normal sale.");

        stokvels.MapPost("/{groupId:guid}/hampers", CreateHamperAsync)
            .RequirePermission(CustomerAccountsPermissions.StokvelManage)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Freezes a hamper basket at a group price.");

        stokvels.MapGet("/{groupId:guid}/statement", GetMemberStatementAsync)
            .RequirePermission(CustomerAccountsPermissions.StokvelView)
            .Produces<MemberStatementResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("One member's statement. Members see only their own line.");

        stokvels.MapGet("/{groupId:guid}/group-statement", GetGroupStatementAsync)
            .RequirePermission(CustomerAccountsPermissions.StokvelView)
            .Produces<GroupStatementResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("The group's statement. Treasurer, chair and secretary only.");

        stokvels.MapPost("/{groupId:guid}/members/{memberId:guid}/remove", RemoveMemberAsync)
            .RequirePermission(CustomerAccountsPermissions.StokvelManage)
            .Produces<decimal>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Marks a member as left and returns the pro-rata refund.");

        return endpoints;
    }

    private static async Task<IResult> CreateGroupAsync(
        CreateStokvelRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<StokvelType>(request.Type, ignoreCase: true, out var type))
        {
            return Results.UnprocessableEntity(new { error = $"Unknown stokvel type '{request.Type}'." });
        }

        Guid id = await dispatcher.SendAsync(
            new CreateStokvelGroupCommand(
                request.Name, type, request.Constitution,
                request.CycleStart, request.CycleEnd, request.StoreId,
                request.CompanyId),
            cancellationToken);
        return Results.Created($"/api/v1/stokvels/{id}", id);
    }

    private static async Task<IResult> GetGroupAsync(
        Guid groupId, IStokvelGroupRepository groups, CancellationToken cancellationToken)
    {
        var group = await groups.FindAsync(groupId, cancellationToken);
        if (group is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new StokvelResponse(
            group.Id, group.GroupNumber, group.Name, group.Type.ToString(),
            group.Status.ToString(), group.CycleStart, group.CycleEnd));
    }

    private static async Task<IResult> AddMemberAsync(
        Guid groupId, AddMemberRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<MemberRole>(request.Role, ignoreCase: true, out var role))
        {
            return Results.UnprocessableEntity(new { error = $"Unknown member role '{request.Role}'." });
        }

        Guid id = await dispatcher.SendAsync(
            new AddStokvelMemberCommand(
                groupId, request.PartnerId, role,
                request.ObligationAmount, request.ObligationCurrency),
            cancellationToken);
        return Results.Created($"/api/v1/stokvels/{groupId}/members/{id}", id);
    }

    private static async Task<IResult> RecordContributionAsync(
        Guid groupId, RecordContributionRequest request, IDispatcher dispatcher,
        IStokvelContributionRepository ledger, IClock clock, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(
            new RecordContributionCommand(
                groupId, request.MemberId, request.Amount, request.Currency,
                request.Channel, request.ReceiptReference, request.TakenOffline),
            cancellationToken);

        var row = await ledger.FindAsync(id, cancellationToken);
        decimal running = 0m;
        if (row is not null)
        {
            IReadOnlyList<StokvelContribution> mine =
                await ledger.ListForMemberAsync(request.MemberId, cancellationToken);
            running = mine.Sum(c => c.Amount.Amount);
        }

        return Results.Created(
            $"/api/v1/stokvels/{groupId}/contributions/{id}",
            new ContributionResponse(
                id, request.MemberId, request.Amount, request.ReceiptReference,
                clock.UtcNow, running));
    }

    private static async Task<IResult> AllocateBenefitsAsync(
        Guid groupId, AllocateBenefitsRequest request, IDispatcher dispatcher,
        IStokvelContributionRepository ledger, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids = await dispatcher.SendAsync(
            new AllocateBenefitsCommand(
                groupId, request.BonusPoolAmount, request.BonusPoolCurrency, request.AsAt),
            cancellationToken);

        var responses = new List<BenefitAllocationResponse>();
        foreach (Guid id in ids)
        {
            var rows = await ledger.ListBenefitsForGroupAsync(groupId, cancellationToken);
            var row = rows.FirstOrDefault(b => b.Id == id);
            if (row is not null)
            {
                responses.Add(new BenefitAllocationResponse(row.Id, row.MemberId, row.Amount.Amount, row.Basis));
            }
        }

        return Results.Created($"/api/v1/stokvels/{groupId}/allocate-benefits", ids);
    }

    private static async Task<IResult> RequestPayoutAsync(
        Guid groupId, RequestPayoutRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<StokvelPayoutKind>(request.Kind, ignoreCase: true, out var kind))
        {
            return Results.UnprocessableEntity(new { error = $"Unknown payout kind '{request.Kind}'." });
        }

        Guid id = await dispatcher.SendAsync(
            new RequestPayoutCommand(
                groupId, request.MemberId, kind, request.Amount, request.Currency,
                request.HamperBasketId, request.CapturedOffline),
            cancellationToken);
        return Results.Created($"/api/v1/stokvels/payouts/{id}", id);
    }

    private static async Task<IResult> ApprovePayoutAsync(
        Guid payoutId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ApprovePayoutCommand(payoutId), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> SettlePayoutAsync(
        Guid payoutId, IDispatcher dispatcher, IStokvelPayoutRepository payouts,
        CancellationToken cancellationToken, bool capturedOffline = false)
    {
        Guid id = await dispatcher.SendAsync(
            new SettlePayoutCommand(payoutId, capturedOffline), cancellationToken);
        var payout = await payouts.FindAsync(id, cancellationToken);
        return Results.Created(
            $"/api/v1/stokvels/payouts/{id}",
            new PayoutResponse(
                id,
                payout?.MemberId ?? Guid.Empty,
                payout?.Kind.ToString() ?? string.Empty,
                payout?.Amount.Amount ?? 0m,
                payout?.Status.ToString() ?? string.Empty,
                payout?.SaleId));
    }

    private static async Task<IResult> CreateHamperAsync(
        Guid groupId, CreateHamperRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(
            new CreateHamperBasketCommand(
                groupId, request.Name, request.GroupPriceAmount, request.GroupPriceCurrency,
                request.ValidFrom, request.ValidTo, request.LocationCode,
                [.. request.Lines.Select(l => new HamperLineInput(
                    l.ItemId, l.ItemVariantId, l.Quantity, l.Uom,
                    l.SubstitutionItemId, l.SubstitutionItemVariantId))],
                request.CompanyId),
            cancellationToken);
        return Results.Created($"/api/v1/stokvels/{groupId}/hampers/{id}", id);
    }

    private static async Task<IResult> GetMemberStatementAsync(
        Guid groupId, IDispatcher dispatcher, CancellationToken cancellationToken,
        Guid? memberId = null, Guid? callerPartnerId = null, string? callerRole = null)
    {
        if (memberId is null || callerPartnerId is null)
        {
            return Results.UnprocessableEntity(new { error = "memberId and callerPartnerId are required." });
        }

        if (!Enum.TryParse<MemberRole>(callerRole ?? "Member", ignoreCase: true, out var role))
        {
            return Results.UnprocessableEntity(new { error = $"Unknown member role '{callerRole}'." });
        }

        MemberStatementResult result = await dispatcher.QueryAsync(
            new GetMemberStatementQuery(groupId, memberId.Value, callerPartnerId.Value, role),
            cancellationToken);
        return Results.Ok(new MemberStatementResponse(
            result.GroupId, result.MemberId, result.Available.Amount,
            [.. result.Lines.Select(l => new StokvelStatementLineResponse(
                l.When, l.Description, l.Debit.Amount, l.Credit.Amount, l.RunningBalance.Amount))]));
    }

    private static async Task<IResult> GetGroupStatementAsync(
        Guid groupId, IDispatcher dispatcher, CancellationToken cancellationToken,
        string? callerRole = null)
    {
        if (!Enum.TryParse<MemberRole>(callerRole ?? "Treasurer", ignoreCase: true, out var role))
        {
            return Results.UnprocessableEntity(new { error = $"Unknown member role '{callerRole}'." });
        }

        GroupStatementResult result = await dispatcher.QueryAsync(
            new GetGroupStatementQuery(groupId, role), cancellationToken);
        return Results.Ok(new GroupStatementResponse(
            result.GroupId, result.GroupBalance.Amount,
            [.. result.Members.Select(m => new GroupMemberResponse(
                m.MemberId, m.PartnerId, m.Role, m.Available.Amount))]));
    }

    private static async Task<IResult> RemoveMemberAsync(
        Guid groupId, Guid memberId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Money refund = await dispatcher.SendAsync(
            new RemoveMemberCommand(groupId, memberId), cancellationToken);
        return Results.Ok(refund.Amount);
    }
}
