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
        return endpoints;
    }

    private static async Task<IResult> FetchDocumentAsync(string token, IDocumentDeliveryService delivery, IClock clock, CancellationToken cancellationToken)
    {
        string? documentReference = await delivery.FetchAsync(token, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return documentReference is null
            ? Results.NotFound()
            : Results.Ok(new { documentReference });
    }

    private static async Task<IResult> WhatsAppWebhookAsync(
        HttpRequest request,
        IConfiguration configuration,
        ILoggerFactory loggers,
        IContactResolver contacts,
        IIntentClassifier classifier,
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
            : await InboundAsync(message, contacts, classifier, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> InboundAsync(InboundMessage message, IContactResolver contacts, IIntentClassifier classifier, CancellationToken cancellationToken)
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
        IntentClassification classification = await classifier.ClassifyAsync(message.Text, cancellationToken).ConfigureAwait(false);
        return Results.Accepted(value: new { bindingId = binding.Id, classification.Intent, classification.Confidence });
    }

    public sealed record InboundMessage(ConversationChannel Channel, string Address, string Text);
}
