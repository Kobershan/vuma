using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Workflow;
using VumaRetail.Workflow.Approvals;
using VumaRetail.Workflow.Documents;
using VumaRetail.Workflow.Notifications;
using VumaRetail.Workflow.Permissions;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers Stage 05's approvals, notifications and document primitives.</summary>
public static class WorkflowServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IApprovalService"/>, <see cref="INotificationDispatcher"/> and
    /// <see cref="IDocumentService"/>, their repositories, and the working in-process channel and
    /// blob-store adapters.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="documentStore">Where the filesystem document blob store keeps its objects.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// Call after <c>AddVumaPersistence</c>: the repositories read the <c>DbContext</c>.
    /// <see cref="ApprovalEngine"/> also depends on Stage 02's <c>IRoleRepository</c>, so this should
    /// follow whatever registers identity too — <c>AddVumaWeb</c> does, in every host that calls it.
    /// </remarks>
    public static IServiceCollection AddVumaWorkflow(
        this IServiceCollection services,
        DocumentBlobStoreOptions documentStore)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(documentStore);

        services.AddSingleton(documentStore);
        services.AddSingleton<IDocumentBlobStore>(new FileSystemDocumentBlobStore(documentStore));
        services.AddSingleton<IDocumentRenderer, PassthroughDocumentRenderer>();

        services.AddScoped<IApprovalPolicyRepository, ApprovalPolicyRepository>();
        services.AddScoped<IApprovalRequestRepository, ApprovalRequestRepository>();
        services.AddScoped<IApprovalDecisionRepository, ApprovalDecisionRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IDocumentVersionRepository, DocumentVersionRepository>();

        // Scoped: the single choke point rule 13 requires, resolved per request like every other
        // Application-facing service in this codebase (IEntitlementService, IBackupService).
        services.AddScoped<IApprovalService, ApprovalEngine>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        services.TryAddEnumerable(
        [
            ServiceDescriptor.Scoped<INotificationChannel, InAppNotificationChannel>(),
            ServiceDescriptor.Scoped<INotificationChannel, LogEmailNotificationChannel>(),
            ServiceDescriptor.Scoped<INotificationChannel, LogPushNotificationChannel>(),
        ]);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, WorkflowPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, WorkflowModuleManifest>());

        // The workflow handlers and validators live in VumaRetail.Workflow, which the
        // Application-only scan does not reach.
        services.AddVumaMessaging(typeof(VumaRetail.Workflow.AssemblyMarker).Assembly);

        return services;
    }
}
