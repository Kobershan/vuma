using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Loyalty.Commands;
using VumaRetail.Application.Loyalty.Permissions;
using VumaRetail.Application.Loyalty.Queries;
using static VumaRetail.PublicApi.Loyalty.LoyaltyPublicContracts;

namespace VumaRetail.PublicApi.Loyalty;

/// <summary>Options for the public loyalty surface (Stage 20).</summary>
public sealed class LoyaltyPublicOptions
{
    /// <summary>Shared secret for Orbit webhook HMAC-SHA256. Empty in dev (accept + warn).</summary>
    public string WebhookSecret { get; init; } = string.Empty;
}

/// <summary>Display formatting for the public surface (Stage 20).</summary>
/// <remarks>
/// Presentation only: stored points stay scale-4 in the domain; this renders 2dp for shoppers,
/// midpoints away from zero like a till receipt. The domain's own display rule matches this by
/// construction (covered by the calculator tests), but this host never reaches into it (ADR-021).
/// </remarks>
public static class LoyaltyDisplay
{
    /// <summary>Renders stored points at 2dp.</summary>
    /// <param name="stored">Points at scale 4.</param>
    public static decimal ToDisplay(decimal stored)
        => decimal.Round(stored, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Vuma's public loyalty surface: members, earn/burn, balances, tiers, rewards and the Orbit
/// webhook receiver (Stage 20). DTOs and mappings live here (ADR-021); the routes are served by
/// any host with persistence — today the store server — while the standalone API-key host
/// follows with Stage 30b's auth (ADR-151).
/// </summary>
public static class LoyaltyEndpoints
{
    private const string Module = "loyalty";
    private const string CustomerClaim = "vuma_customer";
    private const string TerminalClaim = "vuma_terminal";
    private const string IdempotencyHeader = "Idempotency-Key";
    private const string SignatureHeader = "X-Orbit-Signature";

    /// <summary>Maps the public loyalty endpoints under <c>/api/v1/loyalty</c>.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="options">Webhook and surface options.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaLoyaltyPublic(
        this IEndpointRouteBuilder endpoints, LoyaltyPublicOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        LoyaltyPublicOptions surface = options ?? new LoyaltyPublicOptions();
        var limiter = new LoyaltyRateLimiter();

        RouteGroupBuilder loyalty = endpoints.MapGroup("/api/v1/loyalty").WithTags("Loyalty");

        loyalty.MapPost("/members/{customerId:guid}/enroll", EnrollMemberAsync)
            .RequirePermission(LoyaltyPermissions.MemberEnroll)
            .Produces<MemberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Enrolls a customer as a loyalty member.");

        loyalty.MapGet("/members/{customerId:guid}", GetMemberAsync)
            .RequirePermission(LoyaltyPermissions.MemberView)
            .Produces<MemberResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("A member's profile with its honestly-labelled cached balance.");

        loyalty.MapPost("/members/{customerId:guid}/earn", EarnAsync)
            .RequirePermission(LoyaltyPermissions.Earn)
            .Produces<EarnResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Earns points. Unreachable engine → 202 accepted, retrying — the till never blocks.")
            .WithDescription(
                "Replaying an idempotency key returns the original outcome; the same key with a "
                + "different body is refused.");

        loyalty.MapPost("/members/{customerId:guid}/redeem", RedeemAsync)
            .RequirePermission(LoyaltyPermissions.Redeem)
            .Produces<RedeemResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Redeems points. The cache pre-check is provisional; Orbit's debit is authoritative.");

        loyalty.MapGet("/members/{customerId:guid}/balance", GetBalanceAsync)
            .RequirePermission(LoyaltyPermissions.BalanceView)
            .Produces<BalanceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("The cached balance with its age. Stale reads say so on the face.");

        loyalty.MapGet("/members/{customerId:guid}/transactions", ListTransactionsAsync)
            .RequirePermission(LoyaltyPermissions.BalanceView)
            .Produces<IReadOnlyList<TransactionResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("A member's transaction history, newest first.");

        loyalty.MapGet("/members/{customerId:guid}/tier", GetMemberTierAsync)
            .RequirePermission(LoyaltyPermissions.MemberView)
            .Produces<MemberTierResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("A member's tier and progress to the next one.");

        loyalty.MapGet("/tiers", ListTiersAsync)
            .RequirePermission(LoyaltyPermissions.CatalogueView)
            .Produces<IReadOnlyList<TierResponse>>()
            .WithSummary("Cached tiers, threshold ascending.");

        loyalty.MapGet("/rewards", ListRewardsAsync)
            .RequirePermission(LoyaltyPermissions.CatalogueView)
            .Produces<IReadOnlyList<RewardResponse>>()
            .WithSummary("Cached rewards. Cost and availability re-verify at redemption.");

        loyalty.MapPost("/catalogue/sync", SyncCatalogueAsync)
            .RequirePermission(LoyaltyPermissions.Admin)
            .Produces<CatalogueSyncResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Pulls Orbit's tiers and rewards into the local cache.");

        loyalty.MapPost("/settings", ConfigureLoyaltyAsync)
            .RequirePermission(LoyaltyPermissions.Admin)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Configures the loyalty programme for a company.");

        loyalty.MapPost("/webhooks/notifications", WebhookAsync)
            .AllowAnonymous()
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithSummary("Receives Orbit tier/balance events. HMAC-signed; unknown members ignored.");

        // Cross-cutting filters for the whole group, outermost first: entitlement, member scope,
        // neutral read-only, rate limits.
        loyalty.AddEndpointFilter(async (context, next) =>
            await LoyaltyModuleFilterAsync(context, next).ConfigureAwait(false));
        loyalty.AddEndpointFilter(async (context, next) =>
            await MemberScopeFilterAsync(context, next).ConfigureAwait(false));
        loyalty.AddEndpointFilter(async (context, next) =>
            await NeutralReadOnlyFilterAsync(context, next).ConfigureAwait(false));
        loyalty.AddEndpointFilter(async (context, next) =>
            await RateLimitFilterAsync(context, next, limiter).ConfigureAwait(false));

        return endpoints;
    }

    private static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        // Same policy the staff surface uses (vuma:perm:{permission}), resolved by the serving
        // host — this assembly never touches the Web project that defines the helper (ADR-021).
        builder.RequireAuthorization("vuma:perm:" + permission);
        return builder;
    }

    private static async ValueTask<object?> LoyaltyModuleFilterAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // The webhook carries HMAC auth, not a licence context — it must never be gated on the
        // tenant's modules.
        if (IsWebhook(context))
        {
            return await next(context).ConfigureAwait(false);
        }

        IEntitlementService entitlements = context.HttpContext.RequestServices
            .GetRequiredService<IEntitlementService>();

        if (!await entitlements.IsModuleEnabledAsync(Module, context.HttpContext.RequestAborted))
        {
            throw new ModuleNotEnabledException(Module);
        }

        return await next(context).ConfigureAwait(false);
    }

    private static ValueTask<object?> MemberScopeFilterAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // Member tokens (Stage 30b) scope a shopper to their own record; staff JWTs carry no such
        // claim and pass untouched. Answered as not-found so one member cannot probe another's
        // existence (API_STANDARDS.md §4).
        if (context.HttpContext.Request.RouteValues.TryGetValue("customerId", out object? route)
            && route is not null
            && Guid.TryParse(route.ToString(), out Guid target)
            && context.HttpContext.User.FindFirst(CustomerClaim) is { Value: string caller }
            && Guid.TryParse(caller, out Guid callerId)
            && callerId != target)
        {
            return ValueTask.FromResult<object?>(Results.NotFound());
        }

        return next(context);
    }

    private static async ValueTask<object?> NeutralReadOnlyFilterAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (LicenceReadOnlyException)
        {
            return ReadOnlyProblem();
        }
    }

    /// <summary>
    /// The neutral read-only answer for public writes (TESTING.md §7): 503 with no hint of the
    /// tenant's billing status. A fresh document, never the exception's own — it carries amounts
    /// and links that belong on the licence screen, not on a shopper's phone.
    /// </summary>
    public static IResult ReadOnlyProblem() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Temporarily unavailable",
        detail: "The loyalty service is temporarily unavailable. Balances and catalogues "
            + "still serve; please try the request again shortly.",
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "LOYALTY_TEMPORARILY_UNAVAILABLE",
        });

    private static async ValueTask<object?> RateLimitFilterAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next, LoyaltyRateLimiter limiter)
    {
        HttpContext http = context.HttpContext;

        if (IsWebhook(context))
        {
            return await next(context).ConfigureAwait(false);
        }

        string key = RateLimitKey(http);
        int limit = http.User.FindFirst(CustomerClaim) is not null ? 30 : 100;
        IClock clock = http.RequestServices.GetRequiredService<IClock>();

        // Idempotent replays of a completed request are exempt — a legitimate retry is never
        // penalised. Checked against the log, not a local cache, so it holds across restarts.
        if (http.Request.Headers.TryGetValue(IdempotencyHeader, out var rawKey)
            && Guid.TryParse(rawKey.ToString(), out Guid idempotencyKey)
            && context.Arguments.OfType<IDispatcher>().FirstOrDefault() is { } dispatcher
            && await IsReplayAsync(context, dispatcher, idempotencyKey).ConfigureAwait(false))
        {
            return await next(context).ConfigureAwait(false);
        }

        if (!limiter.TryAcquire(key, limit, clock.UtcNow, out TimeSpan retryAfter))
        {
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
            return Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too many requests",
                detail: "The loyalty request rate was exceeded. Retry after the advertised delay.",
                extensions: new Dictionary<string, object?> { ["code"] = "LOYALTY_RATE_LIMITED" });
        }

        return await next(context).ConfigureAwait(false);
    }

    private static bool IsWebhook(EndpointFilterInvocationContext context)
        => context.HttpContext.Request.Path.StartsWithSegments("/api/v1/loyalty/webhooks");

    private static string RateLimitKey(HttpContext http)
    {
        string? terminal = http.User.FindFirst(TerminalClaim)?.Value;
        if (terminal is not null)
        {
            return "terminal:" + terminal;
        }

        string? subject = http.User.FindFirst(CustomerClaim)?.Value
            ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return subject is not null
            ? "caller:" + subject
            : "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }

    private static async Task<bool> IsReplayAsync(
        EndpointFilterInvocationContext context, IDispatcher dispatcher, Guid idempotencyKey)
    {
        try
        {
            Guid? companyId = context.Arguments.OfType<EarnRequest>().FirstOrDefault()?.CompanyId
                ?? context.Arguments.OfType<RedeemRequest>().FirstOrDefault()?.CompanyId;

            if (companyId is null)
            {
                return false;
            }

            TransactionReplayEntry? entry = await dispatcher
                .QueryAsync(new GetTransactionByKeyQuery(companyId.Value, idempotencyKey))
                .ConfigureAwait(false);

            return entry is not null && entry.IsTerminal;
        }
        catch (Exception)
        {
            // The exemption is an optimisation, never a gate: when in doubt, count the request.
            return false;
        }
    }

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

    private static async Task<IResult> EnrollMemberAsync(
        Guid customerId,
        EnrollMemberRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);

        await dispatcher
            .SendAsync(new EnrollMemberCommand(request.CompanyId, customerId, request.StoreId), cancellationToken)
            .ConfigureAwait(false);

        LoyaltyMemberEntry member = await dispatcher
            .QueryAsync(new GetMemberQuery(customerId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/loyalty/members/{customerId}", ToResponse(member));
    }

    private static async Task<IResult> GetMemberAsync(
        Guid customerId,
        Guid? companyId,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);

        LoyaltyMemberEntry member = await dispatcher
            .QueryAsync(new GetMemberQuery(customerId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(member));
    }

    private static async Task<IResult> EarnAsync(
        Guid customerId,
        EarnRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);

        EarnOutcome outcome = await dispatcher
            .SendAsync(
                new EarnPointsCommand(
                    request.CompanyId, customerId, request.PurchaseAmount, request.Currency,
                    request.Reference, request.IdempotencyKey, request.IsMarketingBonus),
                cancellationToken)
            .ConfigureAwait(false);

        var response = new EarnResponse(
            outcome.TransactionId, outcome.Points,
            LoyaltyDisplay.ToDisplay(outcome.NewBalance), outcome.TierId, outcome.Queued);

        // Queued is accepted-for-later-work, not created-now: 202, never 200.
        return outcome.Queued
            ? TypedResults.Accepted($"/api/v1/loyalty/members/{customerId}/transactions", response)
            : TypedResults.Ok(response);
    }

    private static async Task<IResult> RedeemAsync(
        Guid customerId,
        RedeemRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);

        RedeemOutcome outcome = await dispatcher
            .SendAsync(
                new RedeemPointsCommand(
                    request.CompanyId, customerId, request.Points, request.Reference,
                    request.IdempotencyKey),
                cancellationToken)
            .ConfigureAwait(false);

        var response = new RedeemResponse(
            outcome.TransactionId, outcome.Points,
            LoyaltyDisplay.ToDisplay(outcome.NewBalance), outcome.Queued);

        return outcome.Queued
            ? TypedResults.Accepted($"/api/v1/loyalty/members/{customerId}/transactions", response)
            : TypedResults.Ok(response);
    }

    private static async Task<IResult> GetBalanceAsync(
        Guid customerId,
        Guid? companyId,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);

        BalanceEntry balance = await dispatcher
            .QueryAsync(new GetBalanceQuery(customerId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new BalanceResponse(
            balance.CustomerId, LoyaltyDisplay.ToDisplay(balance.Balance),
            balance.AsAt, balance.IsStale));
    }

    private static async Task<IResult> ListTransactionsAsync(
        Guid customerId,
        Guid? companyId,
        int? limit,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);

        IReadOnlyList<LoyaltyTransactionEntry> rows = await dispatcher
            .QueryAsync(new ListTransactionsQuery(customerId, limit), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<TransactionResponse>>(
            [.. rows.Select(row => new TransactionResponse(
                row.Id, row.Type, LoyaltyDisplay.ToDisplay(row.Amount),
                row.Status, row.Reference, row.OccurredAt))]);
    }

    private static async Task<IResult> GetMemberTierAsync(
        Guid customerId,
        Guid? companyId,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);

        MemberTierEntry tier = await dispatcher
            .QueryAsync(new GetMemberTierQuery(customerId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new MemberTierResponse(
            tier.CustomerId, tier.TierId, tier.TierName, tier.NextTierId, tier.PointsToNext,
            LoyaltyDisplay.ToDisplay(tier.Balance)));
    }

    private static async Task<IResult> ListTiersAsync(
        Guid companyId,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);

        IReadOnlyList<TierEntry> rows = await dispatcher
            .QueryAsync(new ListTiersQuery(companyId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<TierResponse>>(
            [.. rows.Select(row => new TierResponse(
                row.TierId, row.Name, row.DisplayName, row.ThresholdPoints, row.Multiplier))]);
    }

    private static async Task<IResult> ListRewardsAsync(
        Guid companyId,
        string? tierId,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);

        IReadOnlyList<RewardEntry> rows = await dispatcher
            .QueryAsync(new ListRewardsQuery(companyId, tierId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<RewardResponse>>(
            [.. rows.Select(row => new RewardResponse(
                row.RewardId, row.Name, row.Description, row.CostInPoints, row.TierId,
                row.IsAvailable))]);
    }

    private static async Task<IResult> SyncCatalogueAsync(
        SyncCatalogueRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);

        CatalogueSyncOutcome outcome = await dispatcher
            .SendAsync(new SyncCatalogueCommand(request.CompanyId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new CatalogueSyncResponse(outcome.Tiers, outcome.Rewards));
    }

    private static async Task<IResult> ConfigureLoyaltyAsync(
        ConfigureLoyaltyRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);

        await dispatcher
            .SendAsync(
                new ConfigureLoyaltyCommand(
                    request.CompanyId, request.Currency, request.EarnRate,
                    request.PointExpiryDays, request.Enabled),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> WebhookAsync(
        HttpContext http,
        IDispatcher dispatcher,
        ICompanyContext company,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        string body;
        using (var reader = new StreamReader(http.Request.Body, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        WebhookNotificationRequest? notification;
        try
        {
            notification = JsonSerializer.Deserialize<WebhookNotificationRequest>(
                body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return TypedResults.BadRequest();
        }

        if (notification is null)
        {
            return TypedResults.BadRequest();
        }

        LoyaltyPublicOptions surface = http.RequestServices
            .GetService<LoyaltyPublicOptions>() ?? new LoyaltyPublicOptions();

        if (!VerifySignature(http, body, surface.WebhookSecret, loggers))
        {
            return TypedResults.Unauthorized();
        }

        BindCompany(company, notification.CompanyId);

        await dispatcher
            .SendAsync(
                new ProcessLoyaltyWebhookCommand(
                    notification.CompanyId, notification.OrbitMemberId, notification.Balance,
                    notification.TierId, notification.EventType),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Accepted((string?)null);
    }

    /// <summary>Verifies an Orbit webhook HMAC-SHA256 signature. Test seam: pure function.</summary>
    /// <param name="http">The request (headers + services for the dev-mode warning).</param>
    /// <param name="body">The raw body.</param>
    /// <param name="secret">The configured secret. Missing configuration rejects the request.</param>
    /// <param name="loggers">Logger factory for the configuration error.</param>
    public static bool VerifySignature(
        HttpContext http, string body, string secret, ILoggerFactory loggers)
    {
        if (string.IsNullOrEmpty(secret))
        {
            // A missing verification secret must never turn an anonymous webhook into an
            // unauthenticated write surface. Operators must configure the integration explicitly.
            loggers.CreateLogger("VumaRetail.PublicApi.Loyalty").LogWarning(
                "Orbit webhook rejected: no verification secret is configured.");
            return false;
        }

        if (!http.Request.Headers.TryGetValue(SignatureHeader, out var presented)
            || !presented.ToString().StartsWith("sha256=", StringComparison.Ordinal))
        {
            return false;
        }

        byte[] expected;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
        {
            expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        }

        byte[] actual;
        try
        {
            actual = Convert.FromHexString(presented.ToString()["sha256=".Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static MemberResponse ToResponse(LoyaltyMemberEntry member) => new(
        member.CustomerId, member.EnrolledAt, member.TierId,
        member.DisplayBalance, member.BalanceCacheAsAt, member.IsStale);
}
