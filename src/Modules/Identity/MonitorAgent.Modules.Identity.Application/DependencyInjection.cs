using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.BuildingBlocks.Application;

namespace MonitorAgent.Modules.Identity.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddHandlersFrom(assembly);
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        return services;
    }
}
