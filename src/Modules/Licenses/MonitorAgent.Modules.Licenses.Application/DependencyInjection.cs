using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Licenses.Contracts;

namespace MonitorAgent.Modules.Licenses.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddLicensesApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddHandlersFrom(assembly);
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddSingleton<CatalogCache>();
        services.AddScoped<CompanyScope>();
        services.AddScoped<ILicenseFulfillment, LicenseFulfillment>();
        services.AddScoped<IPlanCatalog, PlanCatalog>();
        return services;
    }
}
