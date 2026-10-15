using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MonitorAgent.Modules.Releases.Application;
using MonitorAgent.Modules.Releases.Infrastructure.Persistence;

namespace MonitorAgent.Modules.Releases.Infrastructure;

public sealed class ReleasesModuleOptions
{
    /// <summary>Migrate (production and development), EnsureCreated (tests) or None.</summary>
    public string DatabaseInitialization { get; set; } = "None";

    public string PublicKey { get; set; } = "";
}

public static class ReleasesModule
{
    public static IServiceCollection AddReleasesModule(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Releases").Get<ReleasesModuleOptions>() ?? new ReleasesModuleOptions();
        services.AddSingleton(options);
        services.AddSingleton(new ReleasesOptions { PublicKey = options.PublicKey });
        services.AddDbContext<ReleasesDb>(db => db.UseSqlServer(configuration.GetConnectionString("Default"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", ReleasesDb.Schema)));
        services.AddScoped<IReleaseStore, ReleaseStore>();
        services.AddHostedService<ReleasesDbInitializer>();
        services.AddReleasesApplication();
        services.AddControllers().AddApplicationPart(typeof(ReleasesModule).Assembly);
        return services;
    }
}

internal sealed class ReleasesDbInitializer(IServiceScopeFactory scopes, ReleasesModuleOptions options) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReleasesDb>();
        switch (options.DatabaseInitialization)
        {
            case "Migrate": await db.Database.MigrateAsync(ct); break;
            case "EnsureCreated": await db.Database.EnsureCreatedAsync(ct); break;
            case "None": break;
            default: throw new InvalidOperationException($"Unknown Releases:DatabaseInitialization '{options.DatabaseInitialization}'.");
        }
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;
}
