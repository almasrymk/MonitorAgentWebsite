using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.Modules.Identity.Infrastructure.Persistence;

namespace MonitorAgent.Modules.Identity.Infrastructure;

/// <summary>Prepares the schema, makes sure every role exists, and creates the first admin when configured to.</summary>
internal sealed class IdentitySeeder(IServiceScopeFactory scopes, IdentityModuleOptions options, TimeProvider clock, ILogger<IdentitySeeder> logger)
    : IHostedLifecycleService
{
    // Runs before any hosted StartAsync (the data protection key ring reads this database on start).
    public async Task StartingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDb>();
        switch (options.DatabaseInitialization)
        {
            case "Migrate": await db.Database.MigrateAsync(ct); break;
            case "EnsureCreated": await db.Database.EnsureCreatedAsync(ct); break;
            case "None": return;
            default: throw new InvalidOperationException($"Unknown Identity:DatabaseInitialization '{options.DatabaseInitialization}'.");
        }

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var role in IdentityRoles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new ApplicationRole(role));

        if (string.IsNullOrWhiteSpace(options.BootstrapAdminEmail) || string.IsNullOrWhiteSpace(options.BootstrapAdminPassword)) return;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if ((await users.GetUsersInRoleAsync(IdentityRoles.Admin)).Count > 0) return;

        var admin = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = options.BootstrapAdminEmail,
            Email = options.BootstrapAdminEmail,
            EmailConfirmed = true,
            FullName = "Administrator",
            CreatedAt = clock.GetUtcNow(),
        };
        var created = await users.CreateAsync(admin, options.BootstrapAdminPassword);
        if (!created.Succeeded)
            throw new InvalidOperationException("Bootstrap admin rejected: " + string.Join(" ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(admin, IdentityRoles.Admin);
        logger.LogWarning("Created the bootstrap admin account. It must turn on two-step verification before using the admin area.");
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;
}
