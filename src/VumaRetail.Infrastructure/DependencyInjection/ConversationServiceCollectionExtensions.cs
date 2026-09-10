using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Conversations;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers Stage 22b's deterministic conversation boundary.</summary>
public static class ConversationServiceCollectionExtensions
{
    public static IServiceCollection AddVumaConversationalCommerce(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IIntentClassifier, KeywordIntentClassifier>();
        services.AddSingleton<IReplyComposer, TemplateReplyComposer>();
        services.AddSingleton<InMemoryContactResolver>();
        services.AddSingleton<IContactResolver>(sp => sp.GetRequiredService<InMemoryContactResolver>());
        services.AddSingleton<IVerificationService, VerificationService>();
        services.AddSingleton<IDocumentDeliveryService, DocumentDeliveryService>();
        services.AddSingleton<ConversationRateLimiter>();
        services.AddScoped<IConversationStateMachine, ConversationStateMachine>();
        return services;
    }
}
