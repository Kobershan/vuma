using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Conversations;
using VumaRetail.Domain.Conversations;
using VumaRetail.Application.Abstractions;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Conversations;

/// <summary>Transport-neutral inbound conversation routes. Stage 22 owns the actual sender.</summary>
public static class ConversationEndpoints
{
    public static IEndpointRouteBuilder MapVumaConversations(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapVumaApi().MapGroup("/conversations").WithTags("Conversations").RequireModule("conversations");
        group.MapPost("/inbound", InboundAsync).WithSummary("Accepts a normalised inbound conversation message.");
        endpoints.MapVumaApi().MapPost("/conversations/webhook/whatsapp", WhatsAppWebhookAsync)
            .WithTags("Conversations")
            .WithSummary("Accepts a signature-verified WhatsApp webhook.")
            .RequireModule("conversations");
        group.MapPost("/{conversationId:guid}/escalate", (Guid conversationId) => Results.Accepted($"/api/v1/conversations/{conversationId}"));
        endpoints.MapVumaApi().MapGet("/d/{token}", FetchDocumentAsync)
            .WithTags("Conversations")
            .WithSummary("Fetches a one-time, expiring document delivery reference.")
            .RequireModule("conversations");
        RouteGroupBuilder bindings = endpoints.MapVumaApi().MapGroup("/contact-bindings")
            .WithTags("Conversations")
            .RequireModule("conversations");
        bindings.MapGet("", ListBindingsAsync).RequirePermission(ConversationPermissions.BindingManage);
        bindings.MapPost("", CreateBindingAsync).RequirePermission(ConversationPermissions.BindingManage);
        bindings.MapPost("/{bindingId:guid}/challenge", IssueChallengeAsync).RequirePermission(ConversationPermissions.BindingManage);
        bindings.MapPost("/{bindingId:guid}/verify", VerifyBindingAsync).RequirePermission(ConversationPermissions.BindingManage);
        bindings.MapPost("/{bindingId:guid}/consent", SetConsentAsync).RequirePermission(ConversationPermissions.BindingManage);
        bindings.MapPost("/{bindingId:guid}/scopes", AddScopeAsync).RequirePermission(ConversationPermissions.BindingManage);
        bindings.MapGet("/{bindingId:guid}/scopes", ListScopesAsync).RequirePermission(ConversationPermissions.BindingManage);
        bindings.MapDelete("/{bindingId:guid}", RevokeBindingAsync).RequirePermission(ConversationPermissions.BindingManage);
        return endpoints;
    }

    private static async Task<IResult> FetchDocumentAsync(string token, IDocumentDeliveryService delivery, IClock clock, CancellationToken cancellationToken)
    {
        string? documentReference = await delivery.FetchAsync(token, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return documentReference is null
            ? Results.NotFound()
            : Results.Ok(new { documentReference });
    }

    private static async Task<IResult> ListBindingsAsync(ConversationChannel? channel, Guid? contactId, IContactBindingManagementService service, CancellationToken cancellationToken)
        => Results.Ok(await service.ListAsync(channel, contactId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateBindingAsync(CreateBindingRequest request, IContactBindingManagementService service, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (tenant.TenantId == Guid.Empty || request.ContactId == Guid.Empty || string.IsNullOrWhiteSpace(request.Address))
            return Results.BadRequest(new { error = "tenant, contactId and address are required" });
        ContactBinding binding = new(tenant.TenantId, request.Address, request.ContactId, request.Channel);
        await service.CreateAsync(binding, cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/contact-bindings/{binding.Id}", new { binding.Id, binding.Channel, binding.Address, binding.ContactId });
    }

    private static async Task<IResult> IssueChallengeAsync(Guid bindingId, ChallengeRequest request, IContactBindingManagementService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Otp)) return Results.BadRequest(new { error = "otp is required" });
        VerificationChallenge challenge = await service.IssueChallengeAsync(bindingId, request.Otp, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { challengeId = challenge.Id, challenge.ExpiresAt });
    }

    private static async Task<IResult> VerifyBindingAsync(Guid bindingId, VerifyRequest request, IContactBindingManagementService service, CancellationToken cancellationToken)
    {
        if (request.ChallengeId == Guid.Empty || string.IsNullOrWhiteSpace(request.Otp))
            return Results.BadRequest(new { error = "challengeId and otp are required" });
        bool verified = await service.VerifyAsync(bindingId, request.ChallengeId, request.Otp, cancellationToken).ConfigureAwait(false);
        return verified ? Results.NoContent() : Results.UnprocessableEntity(new { error = "verification failed" });
    }

    private static async Task<IResult> RevokeBindingAsync(Guid bindingId, IContactBindingManagementService service, CancellationToken cancellationToken)
        => await service.RevokeAsync(bindingId, cancellationToken).ConfigureAwait(false) ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> SetConsentAsync(Guid bindingId, ConsentRequest request, IContactBindingManagementService service, CancellationToken cancellationToken)
        => await service.SetConsentAsync(bindingId, request.Granted, cancellationToken).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.NotFound();

    private static async Task<IResult> AddScopeAsync(Guid bindingId, ScopeRequest request, IConversationScopeManagementService service, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (bindingId == Guid.Empty || request.CompanyId == Guid.Empty || request.CustomerAccountId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "bindingId, companyId and customerAccountId are required" });
        }

        ConversationAccountScope scope = new(tenant.TenantId, bindingId, request.CompanyId, request.CustomerAccountId);
        await service.AddAsync(scope, cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/contact-bindings/{bindingId}/scopes", new { scope.Id, scope.BindingId, scope.OperatingCompanyId, scope.CustomerAccountId });
    }

    private static async Task<IResult> ListScopesAsync(Guid bindingId, IConversationScopeManagementService service, CancellationToken cancellationToken)
        => Results.Ok(await service.ListAsync(bindingId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> WhatsAppWebhookAsync(
        HttpRequest request,
        IConfiguration configuration,
        ILoggerFactory loggers,
        IContactResolver contacts,
        IContactBindingManagementService bindingManagement,
        IConversationStore conversationStore,
        IIntentClassifier classifier,
        IClock clock,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        string body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        string secret = configuration["Vuma:Conversations:WebhookSecret"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(secret))
        {
            loggers.CreateLogger("VumaRetail.Web.Conversations").LogWarning(
                "WhatsApp webhook accepted without a signature: no secret is configured.");
        }
        else if (!ConversationWebhookSecurity.Verify(body, request.Headers["X-Vuma-Signature"].ToString(), secret))
        {
            return Results.Unauthorized();
        }

        InboundMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<InboundMessage>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "invalid webhook payload" });
        }

        return message is null
            ? Results.BadRequest(new { error = "invalid webhook payload" })
            : await InboundAsync(message, contacts, bindingManagement, conversationStore, classifier, clock, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> InboundAsync(InboundMessage message, IContactResolver contacts, IContactBindingManagementService bindingManagement, IConversationStore conversationStore, IIntentClassifier classifier, IClock clock, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.Address) || string.IsNullOrWhiteSpace(message.Text))
        {
            return Results.BadRequest(new { error = "address and text are required" });
        }
        ContactBinding? binding = await contacts.ResolveAsync(message.Channel, message.Address, cancellationToken).ConfigureAwait(false);
        if (binding is null)
        {
            return Results.Accepted(value: new { state = "onboarding", message = "Ask your account manager to link this address." });
        }
        if (message.Text.Trim().Equals("STOP", StringComparison.OrdinalIgnoreCase))
        {
            await bindingManagement.SetConsentAsync(binding.Id, granted: false, cancellationToken).ConfigureAwait(false);
            return Results.Accepted(value: new { state = "stopped", message = "You have opted out of conversation messages." });
        }
        if (binding.ConsentState == ConversationConsentState.Withdrawn)
        {
            return Results.Accepted(value: new { state = "stopped", message = "This address has opted out of conversation messages." });
        }
        DateTimeOffset at = clock.UtcNow;
        Conversation conversation = await conversationStore.GetOrCreateAsync(binding, message.Channel, at, cancellationToken).ConfigureAwait(false);
        await conversationStore.AddTurnAsync(new ConversationTurn(binding.TenantId, conversation.Id, ConversationTurnDirection.Inbound, message.Text, at), cancellationToken).ConfigureAwait(false);
        IntentClassification classification = await classifier.ClassifyAsync(message.Text, cancellationToken).ConfigureAwait(false);
        return Results.Accepted(value: new { bindingId = binding.Id, classification.Intent, classification.Confidence });
    }

    public sealed record InboundMessage(ConversationChannel Channel, string Address, string Text);
    public sealed record CreateBindingRequest(ConversationChannel Channel, string Address, Guid ContactId);
    public sealed record ChallengeRequest(string Otp);
    public sealed record VerifyRequest(Guid ChallengeId, string Otp);
    public sealed record ConsentRequest(bool Granted);
    public sealed record ScopeRequest(Guid CompanyId, Guid CustomerAccountId);
}
