using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Imports;
using VumaRetail.Application.Imports.Permissions;
using VumaRetail.Application.Imports.Targets;
using VumaRetail.Imports.Readers;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the Stage 11 <c>imports</c> module — readers, targets, the pipeline and its storage.
/// </summary>
public static class ImportsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the three source readers, the five target handlers, the validator, the repositories,
    /// the file store, the cross-module usage probe, the module's permission declaration and its
    /// manifest.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="options">The upload ceilings and file directory, from <c>Vuma:Imports</c>.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Requires <c>AddVumaCatalog</c>, <c>AddVumaPartners</c>, <c>AddVumaInventory</c>,
    /// <c>AddVumaSales</c> and <c>AddVumaFinance</c>.</b> Not because this module reaches into their
    /// schemas — it does not — but because its five target handlers write <em>through</em> their
    /// repositories and their ledger poster (ADR-079), and its batch numbers come from Finance's
    /// document number sequence (ADR-065). An import is a caller of every module it imports into, and
    /// that is exactly what keeps it from being a second way to write the same records.
    /// </para>
    /// <para>
    /// The readers and handlers are registered as <see cref="ServiceDescriptor"/> enumerables so that
    /// the two factories are built from what the host actually wired. A cloud tier that never receives
    /// an upload can register the module without the PDF reader and get a clear "no reader for Pdf" at
    /// the call rather than an obscure failure at container build.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVumaImports(
        this IServiceCollection services, ImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(options ?? new ImportOptions());

        // Readers. Stateless and thread-safe, so one instance each — a reader holds nothing between
        // calls but the bytes it was handed.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IImportSourceReader, CsvImportSourceReader>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IImportSourceReader, ExcelImportSourceReader>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IImportSourceReader, PdfImportSourceReader>());
        services.TryAddSingleton<IOcrTextExtractor, UnavailableOcrTextExtractor>();
        services.TryAddSingleton<IImportSourceReaderFactory, ImportSourceReaderFactory>();

        // Targets. Scoped, because each holds the repositories of the module it writes into, and those
        // are scoped to the request's DbContext.
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IImportTargetHandler, SupplierImportTargetHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IImportTargetHandler, CustomerImportTargetHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IImportTargetHandler, ItemImportTargetHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IImportTargetHandler, StockOnHandImportTargetHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IImportTargetHandler, PriceListLineImportTargetHandler>());
        services.AddScoped<IImportTargetHandlerFactory, ImportTargetHandlerFactory>();

        services.AddScoped<IImportValidator, ImportValidationService>();
        services.AddScoped<IImportContextFactory, ImportBatchContextFactory>();
        services.AddScoped<IImportUsageProbe, ImportUsageProbe>();

        services.AddScoped<IImportBatchRepository, ImportBatchRepository>();
        services.AddScoped<IImportMappingTemplateRepository, ImportMappingTemplateRepository>();
        services.TryAddSingleton<IImportFileStore, FileSystemImportFileStore>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, ImportsPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleManifest, ImportsModuleManifest>());

        return services;
    }
}
