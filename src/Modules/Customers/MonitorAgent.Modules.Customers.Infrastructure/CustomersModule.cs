using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MonitorAgent.Modules.Customers.Application;
using MonitorAgent.Modules.Customers.Contracts;
using MonitorAgent.Modules.Customers.Infrastructure.Persistence;

namespace MonitorAgent.Modules.Customers.Infrastructure;

public sealed class CustomersModuleOptions
{
    /// <summary>Migrate (production and development), EnsureCreated (tests) or None.</summary>
    public string DatabaseInitialization { get; set; } = "None";
}

public static class CustomersModule
{
    public static IServiceCollection AddCustomersModule(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Customers").Get<CustomersModuleOptions>() ?? new CustomersModuleOptions();
        services.AddSingleton(options);

        services.AddDbContext<CustomersDb>(db => db.UseSqlServer(configuration.GetConnectionString("Default"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", CustomersDb.Schema)));
        services.AddScoped<ICustomerAccounts, CustomerAccounts>();
        services.AddScoped<ICompanyDirectory, CompanyDirectory>();
        services.AddScoped<IStaffCompanyReader, StaffCompanyReader>();
        services.AddHostedService<CustomersDbInitializer>();

        services.AddCustomersApplication();
        services.AddControllers().AddApplicationPart(typeof(CustomersModule).Assembly);
        return services;
    }
}

internal sealed class CustomersDbInitializer(IServiceScopeFactory scopes, CustomersModuleOptions options) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomersDb>();
        switch (options.DatabaseInitialization)
        {
            case "Migrate": await db.Database.MigrateAsync(ct); break;
            case "EnsureCreated": await db.Database.EnsureCreatedAsync(ct); break;
            case "None": break;
            default: throw new InvalidOperationException($"Unknown Customers:DatabaseInitialization '{options.DatabaseInitialization}'.");
        }
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;
}
