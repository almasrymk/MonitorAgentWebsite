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
/// The real host over HTTPS, with each module's database on a temporary SQLite file and email captured in
/// <see cref="Emails"/>. Settings can be overridden per factory.
/// When <c>TEST_SQLSERVER</c> holds a SQL Server connection string (no database name), every factory instead gets its
/// own new database on that server, built by the real migrations, exactly as production runs. CI does this on every PR.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@monitoragent.test";
    public const string SalesEmail = "sales@monitoragent.test";
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

    /// <summary>The SQL Server to test against, or null for SQLite.</summary>
    private static readonly string? SqlServer = Environment.GetEnvironmentVariable("TEST_SQLSERVER") is { Length: > 0 } s ? s : null;

    private readonly string _sqlServerDatabase = $"MonitorAgentTests_{Guid.NewGuid():N}";

    private string Initialization => SqlServer is null ? "EnsureCreated" : "Migrate";

    protected virtual IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["ConnectionStrings:Default"] = SqlServer is null ? "" : $"{SqlServer.TrimEnd(';')};Database={_sqlServerDatabase}",
        ["Email:Provider"] = "Smtp",
        ["Email:Smtp:Host"] = "smtp.invalid",
        ["Email:Smtp:From"] = "no-reply@monitoragent.test",
        ["Identity:DatabaseInitialization"] = Initialization,
        ["Customers:DatabaseInitialization"] = Initialization,
        ["Licenses:DatabaseInitialization"] = Initialization,
        ["Licenses:ExpiryReminderMinutes"] = "0",
        ["Commerce:DatabaseInitialization"] = Initialization,
        ["Commerce:FulfillmentRetrySeconds"] = "0",
        ["Commerce:Sandbox:WebhookSecret"] = SandboxSecret,
        ["Releases:DatabaseInitialization"] = Initialization,
        ["Support:DatabaseInitialization"] = Initialization,
        ["Content:DatabaseInitialization"] = Initialization,
        ["Audit:DatabaseInitialization"] = Initialization,
        ["Releases:PublicKey"] = Convert.ToBase64String(ReleaseKey.ExportSubjectPublicKeyInfo()),
        ["Identity:SessionValidationSeconds"] = "0",
        ["Portal:Url"] = "https://portal.test",
        ["Identity:BootstrapAdminEmail"] = AdminEmail,
        ["Identity:BootstrapAdminPassword"] = AdminPassword,
        ["RateLimiting:PermitPerMinute"] = "100000",
        ["RateLimiting:AuthPermitPerMinute"] = "100000",
        ["RateLimiting:FormPermitPerMinute"] = "100000",
        ["Support:DemoRequestsEmail"] = SalesEmail,
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        foreach (var (key, value) in Settings) builder.UseSetting(key, value);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILicensingPlatform>();
            services.AddSingleton<ILicensingPlatform>(Platform);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            if (SqlServer is not null) return;

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
        if (SqlServer is not null) DropSqlServerDatabase();
    }

    private void DropSqlServerDatabase()
    {
        using var connection = new Microsoft.Data.SqlClient.SqlConnection($"{SqlServer!.TrimEnd(';')};Database=master");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID(N'{_sqlServerDatabase}') IS NOT NULL BEGIN ALTER DATABASE [{_sqlServerDatabase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_sqlServerDatabase}]; END";
        command.ExecuteNonQuery();
    }
}

public sealed class CapturedEmails : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();
    private readonly ConcurrentDictionary<string, bool> _unreachable = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<EmailMessage> To(string address) => _sent.Where(m => m.To.Equals(address, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>From now on, sending to this address fails the way an unreachable mail server does.</summary>
    public void FailFor(string address) => _unreachable[address] = true;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (_unreachable.ContainsKey(message.To)) throw new System.Net.Mail.SmtpException("The mail server is unreachable.");
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
