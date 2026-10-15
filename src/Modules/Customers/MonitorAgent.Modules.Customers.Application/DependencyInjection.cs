using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.BuildingBlocks.Application;

namespace MonitorAgent.Modules.Customers.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomersApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddHandlersFrom(assembly);
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddScoped<CompanyView>();
        services.AddScoped<IPersonalDataSource, CompanyPersonalData>();
        return services;
    }
}
