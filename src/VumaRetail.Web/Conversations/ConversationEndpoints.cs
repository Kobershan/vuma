using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
