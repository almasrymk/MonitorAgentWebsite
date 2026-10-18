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
using MonitorAgent.Modules.Customers.Infrastructure.Persistence;
using MonitorAgent.Modules.Identity.Infrastructure.Persistence;
using MonitorAgent.Modules.Licenses.Application;
using MonitorAgent.Modules.Licenses.Infrastructure.Persistence;

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
    private readonly SqliteConnection _customersDb = new("DataSource=:memory:");
    private readonly SqliteConnection _licensesDb = new("DataSource=:memory:");

    public FakeLicensingPlatform Platform { get; } = new();

    /// <summary>An admin session with two-step verification, kept by tests that need staff access.</summary>
    public TestClient? SharedAdmin { get; set; }

    public CapturedEmails Emails { get; } = new();

    protected virtual IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["ConnectionStrings:Default"] = "",
        ["Email:Provider"] = "Smtp",
        ["Email:Smtp:Host"] = "smtp.invalid",
        ["Email:Smtp:From"] = "no-reply@monitoragent.test",
        ["Identity:DatabaseInitialization"] = "EnsureCreated",
        ["Customers:DatabaseInitialization"] = "EnsureCreated",
        ["Licenses:DatabaseInitialization"] = "EnsureCreated",
        ["Identity:SessionValidationSeconds"] = "0",
        ["Portal:Url"] = "https://portal.test",
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
        if (_customersDb.State != System.Data.ConnectionState.Open) _customersDb.Open();
        if (_licensesDb.State != System.Data.ConnectionState.Open) _licensesDb.Open();
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<IdentityDb>>();
            services.RemoveAll<IDbContextOptionsConfiguration<IdentityDb>>();
            services.AddDbContext<IdentityDb>(o => o.UseSqlite(_identityDb));
            services.RemoveAll<DbContextOptions<CustomersDb>>();
            services.RemoveAll<IDbContextOptionsConfiguration<CustomersDb>>();
            services.AddDbContext<CustomersDb>(o => o.UseSqlite(_customersDb));
            services.RemoveAll<DbContextOptions<LicensesDb>>();
            services.RemoveAll<IDbContextOptionsConfiguration<LicensesDb>>();
            services.AddDbContext<LicensesDb>(o => o.UseSqlite(_licensesDb));
            services.RemoveAll<ILicensingPlatform>();
            services.AddSingleton<ILicensingPlatform>(Platform);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    public TestClient CreateTestClient() =>
        new(CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true }), Emails);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        _identityDb.Dispose();
        _customersDb.Dispose();
        _licensesDb.Dispose();
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
