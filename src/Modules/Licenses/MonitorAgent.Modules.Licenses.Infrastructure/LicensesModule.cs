using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MonitorAgent.Modules.Licenses.Application;
using MonitorAgent.Modules.Licenses.Infrastructure.Persistence;
using MonitorAgent.Modules.Licenses.Infrastructure.Platform;

namespace MonitorAgent.Modules.Licenses.Infrastructure;

public sealed class LicensesModuleOptions
{
    /// <summary>Migrate (production and development), EnsureCreated (tests) or None.</summary>
    public string DatabaseInitialization { get; set; } = "None";
    public LicensingPlatformOptions Platform { get; set; } = new();
}

public static class LicensesModule
{
    public static IServiceCollection AddLicensesModule(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection("Licenses").Get<LicensesModuleOptions>() ?? new LicensesModuleOptions();
        if (!environment.IsDevelopment() && !Uri.TryCreate(options.Platform.BaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Set Licenses:Platform:BaseUrl, ServiceEmail and ServicePassword.");
        services.AddSingleton(options);
        services.AddSingleton(options.Platform);

        services.AddDbContext<LicensesDb>(db => db.UseSqlServer(configuration.GetConnectionString("Default"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LicensesDb.Schema)));
        services.AddScoped<ILicenseStore, LicenseStore>();
        services.AddSingleton<IKeyProtector, DataProtectionKeyProtector>();

        services.AddSingleton<PlatformTokens>();
        services.AddTransient<LicensingPlatformAuthHandler>();
        services.AddHttpClient(LicensingPlatformClient.HttpClientName, client =>
            {
                // Development without a LicensingPlatform: calls fail fast and endpoints answer LICENSING_UNAVAILABLE.
                client.BaseAddress = new Uri(string.IsNullOrWhiteSpace(options.Platform.BaseUrl)
                    ? "http://licensing-platform.invalid/" : options.Platform.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(options.Platform.TimeoutSeconds);
            })
            .AddHttpMessageHandler<LicensingPlatformAuthHandler>();
        services.AddSingleton<ILicensingPlatform, LicensingPlatformClient>();
        services.AddHostedService<LicensesDbInitializer>();

        services.AddLicensesApplication();
        services.AddControllers().AddApplicationPart(typeof(LicensesModule).Assembly);
        return services;
    }
}

internal sealed class LicensesDbInitializer(IServiceScopeFactory scopes, LicensesModuleOptions options) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LicensesDb>();
        switch (options.DatabaseInitialization)
        {
            case "Migrate": await db.Database.MigrateAsync(ct); break;
            case "EnsureCreated": await db.Database.EnsureCreatedAsync(ct); break;
            case "None": break;
            default: throw new InvalidOperationException($"Unknown Licenses:DatabaseInitialization '{options.DatabaseInitialization}'.");
        }
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;
}
