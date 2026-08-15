using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions;
using VumaRetail.Infrastructure.Diagnostics;
using VumaRetail.Infrastructure.Messaging;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the CQRS dispatcher, its pipeline, and every handler and validator by assembly scan
/// (ADR-009).
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IDispatcher"/>, the three pipeline behaviours, and the command handlers,
    /// query handlers and validators found in the given assemblies.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="handlerAssemblies">
    /// Assemblies to scan. Defaults to <c>VumaRetail.Application</c>, which is where every handler
    /// lives today; a module that ships its handlers elsewhere passes its own assembly.
    /// </param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Idempotent. It is called by <c>AddVumaIdentity</c> so a host that wires identity gets a
    /// working dispatcher without a second line, and hosts may call it directly.
    /// </para>
    /// <para>
    /// This method replaces the eight hand-written handler registrations Stage 02 left behind. The
    /// difference matters more than the line count: a module author who adds a handler and forgets to
    /// register it gets a working feature rather than a <c>InvalidOperationException</c> at the till.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVumaMessaging(
        this IServiceCollection services,
        params Assembly[] handlerAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(handlerAssemblies);

        services.TryAddScoped<ICorrelationContext, CorrelationContext>();
        services.TryAddScoped<IDispatcher, Dispatcher>();

        // Enumerable try-add, so calling this twice does not run the logging behaviour twice.
        services.TryAddEnumerable(
        [
            ServiceDescriptor.Scoped<IPipelineBehaviour, LoggingBehaviour>(),
            ServiceDescriptor.Scoped<IPipelineBehaviour, ValidationBehaviour>(),
            ServiceDescriptor.Scoped<IPipelineBehaviour, TransactionBehaviour>(),
        ]);

        Assembly[] assemblies = handlerAssemblies.Length > 0
            ? handlerAssemblies
            : [typeof(Application.AssemblyMarker).Assembly];

        services.Scan(scan => scan
            .FromAssemblies(assemblies)
                .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                    .AsImplementedInterfaces()
                    .WithScopedLifetime()
                .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                    .AsImplementedInterfaces()
                    .WithScopedLifetime()
                // Validators are registered only as IValidator<T>. AsImplementedInterfaces would also
                // register AbstractValidator's IEnumerable<IValidationRule>, and a container full of
                // IEnumerable<T> registrations is a container that resolves surprising things.
                .AddClasses(classes => classes.AssignableTo(typeof(IValidator<>)), publicOnly: false)
                    .As(type => type.GetInterfaces().Where(IsValidatorInterface))
                    .WithScopedLifetime());

        return services;
    }

    private static bool IsValidatorInterface(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IValidator<>);
}
