using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Licenses.Application;
using MonitorAgent.Modules.Licenses.Infrastructure.Persistence;
using MonitorAgent.Modules.Licenses.Infrastructure.Platform;

namespace MonitorAgent.Modules.Licenses.Infrastructure;

public sealed class LicensesModuleOptions
{
    /// <summary>Migrate (production and development), EnsureCreated (tests) or None.</summary>
    public string DatabaseInitialization { get; set; } = "None";
    public LicensingPlatformOptions Platform { get; set; } = new();
    /// <summary>How often to look for licenses that need an expiry reminder. 0 turns the reminders off (tests run them directly).</summary>
    public int ExpiryReminderMinutes { get; set; } = 360;
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
        services.AddHostedService<ExpiryReminderWorker>();

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

internal sealed partial class ExpiryReminderWorker(IServiceScopeFactory scopes, LicensesModuleOptions options, ILogger<ExpiryReminderWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.ExpiryReminderMinutes <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.ExpiryReminderMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new SendExpiryRemindersCommand(), stoppingToken);
                if (result is { IsSuccess: true, Value: > 0 }) Sent(logger, result.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Failed(logger, ex);
            }
        }
    }

    [LoggerMessage(LogLevel.Information, "Sent {Count} license expiry reminders")]
    private static partial void Sent(ILogger logger, int count);

    [LoggerMessage(LogLevel.Error, "Expiry reminders failed")]
    private static partial void Failed(ILogger logger, Exception ex);
}
