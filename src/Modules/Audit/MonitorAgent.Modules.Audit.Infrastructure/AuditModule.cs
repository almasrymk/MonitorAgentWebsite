using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Audit.Application;
using MonitorAgent.Modules.Audit.Infrastructure.Persistence;

namespace MonitorAgent.Modules.Audit.Infrastructure;

public sealed class AuditModuleOptions
{
    /// <summary>Migrate (production and development), EnsureCreated (tests) or None.</summary>
    public string DatabaseInitialization { get; set; } = "None";
}

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Audit").Get<AuditModuleOptions>() ?? new AuditModuleOptions();
        services.AddSingleton(options);
        services.AddDbContext<AuditDb>(db => db.UseSqlServer(configuration.GetConnectionString("Default"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AuditDb.Schema)));
        services.AddScoped<IAuditStore, AuditStore>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddHttpContextAccessor();
        services.AddHostedService<AuditDbInitializer>();
        services.AddAuditApplication();
        services.AddControllers().AddApplicationPart(typeof(AuditModule).Assembly);
        return services;
    }
}

internal sealed class AuditDbInitializer(IServiceScopeFactory scopes, AuditModuleOptions options) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDb>();
        switch (options.DatabaseInitialization)
        {
            case "Migrate": await db.Database.MigrateAsync(ct); break;
            case "EnsureCreated": await db.Database.EnsureCreatedAsync(ct); break;
            case "None": break;
            default: throw new InvalidOperationException($"Unknown Audit:DatabaseInitialization '{options.DatabaseInitialization}'.");
        }
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;
}
