using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Reroute.BuildingBlocks.Cqrs;

public static class CqrsServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher, pipeline behaviours, handlers and validators found in <paramref name="assembly"/>.</summary>
    public static IServiceCollection AddCqrs(this IServiceCollection services, Assembly assembly)
    {
        services.AddScoped<IDispatcher, Dispatcher>();

        // Order matters: tracing wraps validation wraps the handler.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TracingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        Type[] handlerInterfaces = [typeof(ICommandHandler<,>), typeof(IQueryHandler<,>)];
        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var @interface in type.GetInterfaces()
                         .Where(i => i.IsGenericType && handlerInterfaces.Contains(i.GetGenericTypeDefinition())))
            {
                services.AddScoped(@interface, type);
            }
        }

        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Scoped, includeInternalTypes: true);
        return services;
    }
}
