using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MonitorAgent.Tests.Api;

public class ReleasesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private async Task<TestClient> AdminAsync()
    {
        if (factory.SharedAdmin is { } admin) return admin;
        admin = factory.CreateTestClient();
        await admin.SignInAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        await admin.EnableMfaAsync();
        return factory.SharedAdmin = admin;
    }

    /// <summary>A manifest in the program's format (UBGMonitor UpdateManifest), with CRLF line endings like a file saved on Windows.</summary>
    private static byte[] Manifest(string version, string product = "MonitorAgent", string url = "https://downloads.example.test/MonitorAgent-win-x64.zip") =>
        Encoding.UTF8.GetBytes($$"""
            {
              "product": "{{product}}",
              "version": "{{version}}",
              "notes": "Notes for {{version}}",
              "packages": {
                "win-x64": { "url": "{{url}}", "sha256": "{{new string('a', 64)}}", "size": 1048576 }
              }
            }
            """.Replace("\n", "\r\n", StringComparison.Ordinal));

    /// <summary>Signs the way tools/UpdateSigner does: ECDSA P-256 over SHA-256, base64.</summary>
    private static string Sign(byte[] manifest, ECDsa? key = null) =>
        Convert.ToBase64String((key ?? ApiFactory.ReleaseKey).SignData(manifest, HashAlgorithmName.SHA256));

    private async Task<HttpResponseMessage> AddAsync(byte[] manifest, string? signature = null, string channel = "stable") =>
        await (await AdminAsync()).PostAsync("/api/v1/admin/releases",
            new { manifestBase64 = Convert.ToBase64String(manifest), signature = signature ?? Sign(manifest), channel });

    private async Task<Guid> PublishedAsync(string version, string channel = "stable")
    {
        var id = (await JsonAsync(await AddAsync(Manifest(version), channel: channel))).GetProperty("id").GetGuid();
        await JsonAsync(await (await AdminAsync()).PostAsync($"/api/v1/admin/releases/{id}/publish"));
        return id;
    }

    private static bool ProgramAccepts(byte[] manifest, string signature)
    {
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(ApiFactory.ReleaseKey.ExportSubjectPublicKeyInfo(), out _);
        return key.VerifyData(manifest, Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256);
    }

    [Fact]
    public async Task The_program_gets_the_published_manifest_byte_for_byte_with_a_signature_it_accepts()
    {
        var manifest = Manifest("7.1.0");
        var id = (await JsonAsync(await AddAsync(manifest, channel: "edge"))).GetProperty("id").GetGuid();
        var anonymous = factory.CreateTestClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/updates/edge/manifest.json")).StatusCode);

        await JsonAsync(await (await AdminAsync()).PostAsync($"/api/v1/admin/releases/{id}/publish"));

        var served = await anonymous.GetAsync("/updates/edge/manifest.json");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        var bytes = await served.Content.ReadAsByteArrayAsync();
        Assert.Equal(manifest, bytes);
        var signature = await anonymous.Http.GetStringAsync("/updates/edge/manifest.json.sig");
        Assert.True(ProgramAccepts(bytes, signature));
    }

    [Fact]
    public async Task The_default_address_serves_the_newest_stable_version()
    {
        await PublishedAsync("2.0.0");
        await PublishedAsync("2.1.0");

        var manifest = await factory.CreateTestClient().Http.GetStringAsync("/updates/manifest.json");

        Assert.Contains("\"version\": \"2.1.0\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_manifest_not_signed_with_the_publisher_key_is_refused()
    {
        var manifest = Manifest("3.0.0");
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Equal("INVALID_RELEASE_SIGNATURE", await TestClient.ErrorCodeAsync(await AddAsync(manifest, Sign(manifest, otherKey))));
        Assert.Equal("INVALID_RELEASE_SIGNATURE", await TestClient.ErrorCodeAsync(await AddAsync(manifest, Sign(Manifest("3.0.1")))));
        Assert.Equal("INVALID_RELEASE_SIGNATURE", await TestClient.ErrorCodeAsync(await AddAsync(manifest, "not-base64!")));
    }

    [Fact]
    public async Task A_signed_manifest_the_program_would_refuse_is_refused_too()
    {
        Assert.Equal("INVALID_MANIFEST", await TestClient.ErrorCodeAsync(await AddAsync(Manifest("3.1.0", product: "Other"))));
        Assert.Equal("INVALID_MANIFEST", await TestClient.ErrorCodeAsync(await AddAsync(Manifest("not-a-version"))));
        Assert.Equal("INVALID_MANIFEST", await TestClient.ErrorCodeAsync(await AddAsync(Manifest("3.1.0", url: "http://downloads.example.test/x.zip"))));
    }

    [Fact]
    public async Task A_version_can_be_added_once_and_only_a_newer_one_published()
    {
        await PublishedAsync("4.2.0", channel: "beta");

        Assert.Equal("RELEASE_EXISTS", await TestClient.ErrorCodeAsync(await AddAsync(Manifest("4.2.0"), channel: "beta")));
        var older = (await JsonAsync(await AddAsync(Manifest("4.1.9"), channel: "beta"))).GetProperty("id").GetGuid();
        Assert.Equal("VERSION_NOT_NEWER", await TestClient.ErrorCodeAsync(await (await AdminAsync()).PostAsync($"/api/v1/admin/releases/{older}/publish")));
    }

    [Fact]
    public async Task Withdrawing_a_bad_version_offers_the_previous_one_again()
    {
        await PublishedAsync("5.0.0", channel: "rc");
        var bad = await PublishedAsync("5.1.0", channel: "rc");

        var withdrawn = await JsonAsync(await (await AdminAsync()).PostAsync($"/api/v1/admin/releases/{bad}/withdraw"));

        Assert.Equal("Withdrawn", withdrawn.GetProperty("status").GetString());
        Assert.Contains("\"version\": \"5.0.0\"", await factory.CreateTestClient().Http.GetStringAsync("/updates/rc/manifest.json"), StringComparison.Ordinal);
        var listed = await JsonAsync(await factory.CreateTestClient().GetAsync("/api/v1/releases"));
        Assert.DoesNotContain(listed.EnumerateArray(), r => r.GetProperty("version").GetString() == "5.1.0");
    }

    [Fact]
    public async Task Drafts_are_hidden_from_the_public_list()
    {
        await JsonAsync(await AddAsync(Manifest("6.0.0"), channel: "nightly"));

        var listed = await JsonAsync(await factory.CreateTestClient().GetAsync("/api/v1/releases"));

        Assert.DoesNotContain(listed.EnumerateArray(), r => r.GetProperty("channel").GetString() == "nightly");
        var all = await JsonAsync(await (await AdminAsync()).GetAsync("/api/v1/admin/releases"));
        var draft = Assert.Single(all.EnumerateArray(), r => r.GetProperty("channel").GetString() == "nightly");
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.Equal("win-x64", draft.GetProperty("packages")[0].GetProperty("platform").GetString());
    }

    [Fact]
    public async Task Customers_cannot_manage_releases()
    {
        var customer = factory.CreateTestClient();
        var email = $"user-{Guid.NewGuid():N}@example.test";
        await customer.SignInAsync(email, await customer.RegisterConfirmedAsync(email));
        var manifest = Manifest("9.9.9");

        var response = await customer.PostAsync("/api/v1/admin/releases", new { manifestBase64 = Convert.ToBase64String(manifest), signature = Sign(manifest) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
