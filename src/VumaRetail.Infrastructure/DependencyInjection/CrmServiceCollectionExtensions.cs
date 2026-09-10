using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Crm;
using VumaRetail.Application.Crm.Permissions;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers CRM: leads, opportunities, activities, segments and consent (Stage 19).</summary>
public static class CrmServiceCollectionExtensions
{
    /// <summary>
    /// Registers the repositories, the consent/segment/360° services and the module's permission
    /// declaration and manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <b>Requires <c>AddVumaPartners</c></b> (conversion verifies the partner link through the
    /// partners read port — a Guid reference, never a cross-schema foreign key, CONVENTIONS.md
    /// §2). Usage metering needs no registration: the daily rollup counts the <c>crm</c>
    /// schema's audit trail by itself (R10: counts only, no business data).
    /// </remarks>
    public static IServiceCollection AddVumaCrm(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<IOpportunityRepository, OpportunityRepository>();
        services.AddScoped<IActivityRepository, ActivityRepository>();
        services.AddScoped<ISegmentRepository, SegmentRepository>();
        services.AddScoped<ISegmentMemberRepository, SegmentMemberRepository>();
        services.AddScoped<IConsentRepository, ConsentRepository>();

        services.AddScoped<IConsentService, ConsentService>();
        services.AddScoped<ISegmentService, SegmentService>();
        services.AddScoped<ICustomer360ViewService, Customer360ViewService>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, CrmPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, CrmModuleManifest>());

        return services;
    }
}
