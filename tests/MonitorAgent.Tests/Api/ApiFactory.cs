using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Identity.Infrastructure.Persistence;

namespace MonitorAgent.Tests.Api;

/// <summary>
/// The real host over HTTPS, with each module's database on an in-memory SQLite connection and email captured
/// in <see cref="Emails"/>. Settings can be overridden per factory.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@monitoragent.test";
    public const string AdminPassword = "Admin-Passw0rd-For-Tests";

    private readonly SqliteConnection _identityDb = new("DataSource=:memory:");

    public CapturedEmails Emails { get; } = new();

    protected virtual IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["ConnectionStrings:Default"] = "",
        ["Email:Provider"] = "Smtp",
        ["Email:Smtp:Host"] = "smtp.invalid",
        ["Email:Smtp:From"] = "no-reply@monitoragent.test",
        ["Identity:DatabaseInitialization"] = "EnsureCreated",
        ["Identity:SessionValidationSeconds"] = "0",
        ["Identity:PortalUrl"] = "https://portal.test",
        ["Identity:BootstrapAdminEmail"] = AdminEmail,
        ["Identity:BootstrapAdminPassword"] = AdminPassword,
        ["RateLimiting:PermitPerMinute"] = "100000",
        ["RateLimiting:AuthPermitPerMinute"] = "100000",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        foreach (var (key, value) in Settings) builder.UseSetting(key, value);
        if (_identityDb.State != System.Data.ConnectionState.Open) _identityDb.Open();
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<IdentityDb>>();
            services.RemoveAll<IDbContextOptionsConfiguration<IdentityDb>>();
            services.AddDbContext<IdentityDb>(o => o.UseSqlite(_identityDb));
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    public TestClient CreateTestClient() =>
        new(CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true }), Emails);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _identityDb.Dispose();
    }
}

public sealed class CapturedEmails : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> To(string address) => _sent.Where(m => m.To.Equals(address, StringComparison.OrdinalIgnoreCase)).ToList();

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
