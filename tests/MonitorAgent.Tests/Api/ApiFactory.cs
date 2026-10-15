using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Commerce.Infrastructure.Persistence;
using MonitorAgent.Modules.Customers.Infrastructure.Persistence;
using MonitorAgent.Modules.Identity.Infrastructure.Persistence;
using MonitorAgent.Modules.Licenses.Application;
using MonitorAgent.Modules.Licenses.Infrastructure.Persistence;
using MonitorAgent.Modules.Releases.Infrastructure.Persistence;
using MonitorAgent.Modules.Support.Infrastructure.Persistence;
using MonitorAgent.Modules.Content.Infrastructure.Persistence;
using MonitorAgent.Modules.Audit.Infrastructure.Persistence;

namespace MonitorAgent.Tests.Api;

/// <summary>
/// The real host over HTTPS, with each module's database on an in-memory SQLite connection and email captured
/// in <see cref="Emails"/>. Settings can be overridden per factory.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@monitoragent.test";
    public const string AdminPassword = "Admin-Passw0rd-For-Tests";

    // Each module's database is a temp file, so every request gets its own connection and real locking:
    // payments are tested under parallel deliveries, which one shared in-memory connection can't model.
    private readonly string _dbFolder = Directory.CreateTempSubdirectory("monitoragent-tests-").FullName;

    public const string SandboxSecret = "sandbox-secret-for-tests";

    /// <summary>Stands in for the publisher's offline signing key (UBGMonitor tools/UpdateSigner).</summary>
    public static System.Security.Cryptography.ECDsa ReleaseKey { get; } = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);

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
        ["Licenses:ExpiryReminderMinutes"] = "0",
        ["Commerce:DatabaseInitialization"] = "EnsureCreated",
        ["Commerce:FulfillmentRetrySeconds"] = "0",
        ["Commerce:Sandbox:WebhookSecret"] = SandboxSecret,
        ["Releases:DatabaseInitialization"] = "EnsureCreated",
        ["Support:DatabaseInitialization"] = "EnsureCreated",
        ["Content:DatabaseInitialization"] = "EnsureCreated",
        ["Audit:DatabaseInitialization"] = "EnsureCreated",
        ["Releases:PublicKey"] = Convert.ToBase64String(ReleaseKey.ExportSubjectPublicKeyInfo()),
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
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<IdentityDb>>();
            services.RemoveAll<IDbContextOptionsConfiguration<IdentityDb>>();
            services.AddDbContext<IdentityDb>(o => o.UseSqlite(Database("identity")));
            services.RemoveAll<DbContextOptions<CustomersDb>>();
            services.RemoveAll<IDbContextOptionsConfiguration<CustomersDb>>();
            services.AddDbContext<CustomersDb>(o => o.UseSqlite(Database("customers")));
            services.RemoveAll<DbContextOptions<LicensesDb>>();
            services.RemoveAll<IDbContextOptionsConfiguration<LicensesDb>>();
            services.AddDbContext<LicensesDb>(o => o.UseSqlite(Database("licenses")));
            services.RemoveAll<DbContextOptions<CommerceDb>>();
            services.RemoveAll<IDbContextOptionsConfiguration<CommerceDb>>();
            services.AddDbContext<CommerceDb>(o => o.UseSqlite(Database("commerce")));
            services.RemoveAll<DbContextOptions<ReleasesDb>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ReleasesDb>>();
            services.AddDbContext<ReleasesDb>(o => o.UseSqlite(Database("releases")));
            services.RemoveAll<DbContextOptions<SupportDb>>();
            services.RemoveAll<IDbContextOptionsConfiguration<SupportDb>>();
            services.AddDbContext<SupportDb>(o => o.UseSqlite(Database("support")));
            services.RemoveAll<DbContextOptions<ContentDb>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ContentDb>>();
            services.AddDbContext<ContentDb>(o => o.UseSqlite(Database("content")));
            services.RemoveAll<DbContextOptions<AuditDb>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuditDb>>();
            services.AddDbContext<AuditDb>(o => o.UseSqlite(Database("audit")));
            services.RemoveAll<ILicensingPlatform>();
            services.AddSingleton<ILicensingPlatform>(Platform);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    private string Database(string module) => $"Data Source={Path.Combine(_dbFolder, module + ".db")};Pooling=False;Default Timeout=30";

    public TestClient CreateTestClient() =>
        new(CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true }), Emails);

    /// <summary>A new staff member with only the given role, signed in with two-step verification.</summary>
    public async Task<TestClient> StaffAsync(string role)
    {
        var client = CreateTestClient();
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        var password = await client.RegisterConfirmedAsync(email);
        await using (var scope = Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            await users.RemoveFromRoleAsync(user, MonitorAgent.Modules.Identity.Contracts.IdentityRoles.Customer);
            await users.AddToRoleAsync(user, role);
        }
        await client.SignInAsync(email, password);
        await client.EnableMfaAsync();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        // Dispose and DisposeAsync both end up here.
        if (Directory.Exists(_dbFolder)) Directory.Delete(_dbFolder, recursive: true);
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
