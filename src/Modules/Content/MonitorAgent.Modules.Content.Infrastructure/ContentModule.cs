using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MonitorAgent.Modules.Content.Application;
using MonitorAgent.Modules.Content.Infrastructure.Persistence;

namespace MonitorAgent.Modules.Content.Infrastructure;

public sealed class ContentModuleOptions
{
    /// <summary>Migrate (production and development), EnsureCreated (tests) or None.</summary>
    public string DatabaseInitialization { get; set; } = "None";
}

public static class ContentModule
{
    public static IServiceCollection AddContentModule(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Content").Get<ContentModuleOptions>() ?? new ContentModuleOptions();
        services.AddSingleton(options);
        services.AddDbContext<ContentDb>(db => db.UseSqlServer(configuration.GetConnectionString("Default"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", ContentDb.Schema)));
        services.AddScoped<IContentStore, ContentStore>();
        services.AddHostedService<ContentDbInitializer>();
        services.AddContentApplication();
        services.AddControllers().AddApplicationPart(typeof(ContentModule).Assembly);
        return services;
    }
}

internal sealed class ContentDbInitializer(IServiceScopeFactory scopes, ContentModuleOptions options) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDb>();
        switch (options.DatabaseInitialization)
        {
            case "Migrate": await db.Database.MigrateAsync(ct); break;
            case "EnsureCreated": await db.Database.EnsureCreatedAsync(ct); break;
            case "None": break;
            default: throw new InvalidOperationException($"Unknown Content:DatabaseInitialization '{options.DatabaseInitialization}'.");
        }
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;
}
