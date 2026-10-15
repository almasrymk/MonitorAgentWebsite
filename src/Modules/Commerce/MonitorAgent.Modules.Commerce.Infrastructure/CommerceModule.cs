using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Commerce.Application;
using MonitorAgent.Modules.Commerce.Infrastructure.Gateways;
using MonitorAgent.Modules.Commerce.Infrastructure.Persistence;

namespace MonitorAgent.Modules.Commerce.Infrastructure;

public sealed class CommerceModuleOptions
{
    /// <summary>Migrate (production and development), EnsureCreated (tests) or None.</summary>
    public string DatabaseInitialization { get; set; } = "None";

    /// <summary>How often paid orders whose license wasn't issued yet are retried.</summary>
    public int FulfillmentRetrySeconds { get; set; } = 60;

    public SandboxGatewayOptions Sandbox { get; set; } = new();
}

public static class CommerceModule
{
    public static IServiceCollection AddCommerceModule(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection("Commerce").Get<CommerceModuleOptions>() ?? new CommerceModuleOptions();
        services.AddSingleton(options);

        services.AddDbContext<CommerceDb>(db => db.UseSqlServer(configuration.GetConnectionString("Default"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", CommerceDb.Schema)));
        services.AddScoped<ICommerceStore, CommerceStore>();
        services.AddScoped<IPersonalDataSource, OrdersPersonalData>();

        if (!string.IsNullOrEmpty(options.Sandbox.WebhookSecret))
        {
            // Anyone holding the sandbox secret could mark orders paid, so it never runs outside development.
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("Commerce:Sandbox is for development only. Remove its WebhookSecret.");
            services.AddSingleton(options.Sandbox);
            services.AddSingleton<IPaymentGateway, SandboxGateway>();
        }

        services.AddHostedService<CommerceDbInitializer>();
        services.AddHostedService<FulfillmentRetryWorker>();
        services.AddCommerceApplication();
        services.AddControllers().AddApplicationPart(typeof(CommerceModule).Assembly);
        return services;
    }
}

internal sealed class CommerceDbInitializer(IServiceScopeFactory scopes, CommerceModuleOptions options) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommerceDb>();
        switch (options.DatabaseInitialization)
        {
            case "Migrate": await db.Database.MigrateAsync(ct); break;
            case "EnsureCreated": await db.Database.EnsureCreatedAsync(ct); break;
            case "None": break;
            default: throw new InvalidOperationException($"Unknown Commerce:DatabaseInitialization '{options.DatabaseInitialization}'.");
        }
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Issues licenses for orders that were paid while LicensingPlatform was unreachable.</summary>
internal sealed partial class FulfillmentRetryWorker(IServiceScopeFactory scopes, CommerceModuleOptions options, ILogger<FulfillmentRetryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.FulfillmentRetrySeconds <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.FulfillmentRetrySeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new FulfillPaidOrdersCommand(), stoppingToken);
                if (result is { IsSuccess: true, Value: > 0 }) Fulfilled(logger, result.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RetryFailed(logger, ex);
            }
        }
    }

    [LoggerMessage(LogLevel.Information, "Issued licenses for {Count} paid orders")]
    private static partial void Fulfilled(ILogger logger, int count);

    [LoggerMessage(LogLevel.Error, "Fulfilment retry failed")]
    private static partial void RetryFailed(ILogger logger, Exception ex);
}
