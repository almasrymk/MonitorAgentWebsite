using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using MonitorAgent.Modules.Identity.Infrastructure;
using MonitorAgent.Modules.Identity.Infrastructure.Persistence;

namespace MonitorAgent.Tests.Api;

public sealed class KeyProtectionFactory : ApiFactory
{
    public const string CertificatePassword = "certificate-password-for-tests";

    public string CertificatePath { get; } = CreateCertificate();

    protected override IDictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["Identity:KeyProtection:CertificatePath"] = CertificatePath;
            settings["Identity:KeyProtection:CertificatePassword"] = CertificatePassword;
            return settings;
        }
    }

    public static string CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=MonitorAgent data protection (tests)", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var path = Path.Combine(Directory.CreateTempSubdirectory("monitoragent-keys-").FullName, "dataprotection.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, CertificatePassword));
        return path;
    }
}

public class KeyProtectionTests(KeyProtectionFactory factory) : IClassFixture<KeyProtectionFactory>
{
    [Fact]
    public async Task The_signing_keys_are_stored_encrypted_and_sign_in_still_works()
    {
        var client = factory.CreateTestClient();
        var email = $"keys-{Guid.NewGuid():N}@example.test";
        var password = await client.RegisterConfirmedAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/v1/account/me")).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var keys = await scope.ServiceProvider.GetRequiredService<IdentityDb>().DataProtectionKeys.ToListAsync();
        Assert.NotEmpty(keys);
        Assert.All(keys, key =>
        {
            Assert.Contains("encryptedSecret", key.Xml);
            Assert.DoesNotContain("<masterKey", key.Xml);
        });
    }

    [Fact]
    public void Production_refuses_to_start_without_a_certificate()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddIdentityModule(configuration, new Environment(Environments.Production)));
        Assert.Contains("KeyProtection", error.Message);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "MonitorAgent.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
