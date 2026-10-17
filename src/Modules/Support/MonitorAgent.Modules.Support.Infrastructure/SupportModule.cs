using MonitorAgent.BuildingBlocks.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MonitorAgent.Modules.Support.Application;
using MonitorAgent.Modules.Support.Infrastructure.Persistence;

namespace MonitorAgent.Modules.Support.Infrastructure;

public sealed class SupportModuleOptions
{
    /// <summary>Migrate (production and development), EnsureCreated (tests) or None.</summary>
    public string DatabaseInitialization { get; set; } = "None";

    /// <summary>Who hears about new demo requests from the public site, for example sales@. Empty: only the admin panel shows them.</summary>
    public string? DemoRequestsEmail { get; set; }
}

public static class SupportModule
{
    public static IServiceCollection AddSupportModule(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Support").Get<SupportModuleOptions>() ?? new SupportModuleOptions();
        services.AddSingleton(options);
        services.AddSingleton(new SupportNotifications(options.DemoRequestsEmail));
        services.AddDbContext<SupportDb>(db => db.UseSqlServer(configuration.GetConnectionString("Default"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SupportDb.Schema)));
        services.AddScoped<ISupportStore, SupportStore>();
        services.AddScoped<IPersonalDataSource, SupportPersonalData>();
        services.AddHostedService<SupportDbInitializer>();
        services.AddSupportApplication();
        services.AddControllers().AddApplicationPart(typeof(SupportModule).Assembly);
        return services;
    }
}

internal sealed class SupportDbInitializer(IServiceScopeFactory scopes, SupportModuleOptions options) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDb>();
        switch (options.DatabaseInitialization)
        {
            case "Migrate": await db.Database.MigrateAsync(ct); break;
            case "EnsureCreated": await db.Database.EnsureCreatedAsync(ct); break;
            case "None": break;
            default: throw new InvalidOperationException($"Unknown Support:DatabaseInitialization '{options.DatabaseInitialization}'.");
        }
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;
}
