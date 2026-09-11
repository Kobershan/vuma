using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Conversations;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.Infrastructure.Conversations;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers Stage 22b's deterministic conversation boundary.</summary>
public static class ConversationServiceCollectionExtensions
{
    public static IServiceCollection AddVumaConversationalCommerce(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IIntentClassifier, KeywordIntentClassifier>();
        services.AddSingleton<IReplyComposer, TemplateReplyComposer>();
        services.AddScoped<IContactResolver, EfContactResolver>();
        services.AddScoped<IContactBindingManagementService, ContactBindingManagementService>();
        services.AddScoped<ConversationScopeManagementService>();
        services.AddScoped<IConversationScopeManagementService>(sp => sp.GetRequiredService<ConversationScopeManagementService>());
        services.AddScoped<IConversationScopeReader>(sp => sp.GetRequiredService<ConversationScopeManagementService>());
        services.AddSingleton<IVerificationService, VerificationService>();
        services.AddScoped<IDocumentDeliveryTokenStore, EfDocumentDeliveryTokenStore>();
        services.AddScoped<IConversationStore, EfConversationStore>();
        services.AddScoped<IDocumentDeliveryService, DocumentDeliveryService>();
        services.AddSingleton<ConversationRateLimiter>();
        services.AddScoped<IConversationIntentRouter, ConversationIntentRouter>();
        services.AddScoped<IConversationIntentHandler, OrderStatusIntentHandler>();
        services.AddScoped<IConversationIntentHandler, StatementIntentHandler>();
        services.AddScoped<IConversationIntentHandler, InvoiceCopyIntentHandler>();
        services.AddScoped<IConversationIntentHandler, PodIntentHandler>();
        services.AddScoped<IConversationIntentHandler, CreditNoteRequestIntentHandler>();
        services.AddScoped<IConversationIntentHandler, PlaceOrderIntentHandler>();
        services.AddScoped<IConversationStateMachine, ConversationStateMachine>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, ConversationPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, ConversationModuleManifest>());
        return services;
    }
}
