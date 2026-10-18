using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.Modules.Licenses.Application;
using MonitorAgent.Modules.Licenses.Infrastructure.Platform;

namespace MonitorAgent.Tests.Unit;

/// <summary>The real HTTP client against a scripted LicensingPlatform, using the response shapes from its v1 API.</summary>
public class LicensingPlatformClientTests
{
    private sealed class ScriptedPlatform : HttpMessageHandler
    {
        public List<(string Method, string Path, string? Bearer)> Calls { get; } = [];
        public int Logins { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.Parameter));
            if (Override?.Invoke(request) is { } custom) return Task.FromResult(custom);
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path switch
            {
                "/api/v1/auth/login" => Json($$"""{"accessToken":"token-{{++Logins}}","expiresAt":"2999-01-01T00:00:00Z","refreshToken":"r","user":null}"""),
                "/api/v1/plans" => Json("""
                    {"items":[
                      {"id":"11111111-1111-1111-1111-111111111111","productCode":"UBG","productName":"UBG Monitor","code":"STD","name":"Standard","version":2,"status":"Published","price":1200.0,"currency":"EGP","durationDays":365,"maxActivations":3,"features":["alerts"],"kind":"Paid","isCurrent":false},
                      {"id":"22222222-2222-2222-2222-222222222222","productCode":"UBG","productName":"UBG Monitor","code":"FREE","name":"Free","version":1,"status":"Published","price":0,"currency":"EGP","durationDays":null,"maxActivations":1,"features":[],"kind":"Free","isCurrent":true},
                      {"id":"33333333-3333-3333-3333-333333333333","productCode":"UBG","productName":"UBG Monitor","code":"STD","name":"Standard","version":1,"status":"Published","price":1000.0,"currency":"EGP","durationDays":365,"maxActivations":3,"features":[],"kind":"Paid","isCurrent":false},
                      {"id":"88888888-8888-8888-8888-888888888888","productCode":"UBG","productName":"UBG Monitor","code":"STD","name":"Standard","version":3,"status":"Draft","price":1500.0,"currency":"EGP","durationDays":365,"maxActivations":3,"features":[],"kind":"Paid","isCurrent":false},
                      {"id":"99999999-9999-9999-9999-999999999999","productCode":"UBG","productName":"UBG Monitor","code":"PRO","name":"Pro","version":1,"status":"Published","price":2500.0,"currency":"EGP","durationDays":365,"maxActivations":10,"features":["alerts","reports"],"kind":"Paid","isCurrent":false}
                    ],"total":5,"page":1,"pageSize":200}
                    """),
                "/api/v1/licenses" when request.Method == HttpMethod.Post => Json($$"""{"license":{{License}},"productKey":"UBG-FULL-KEY" }""", HttpStatusCode.Created),
                _ when path.StartsWith("/api/v1/licenses/", StringComparison.Ordinal) && path.EndsWith("/reset", StringComparison.Ordinal) => new HttpResponseMessage(HttpStatusCode.NoContent),
                _ when path == "/api/v1/licenses/44444444-4444-4444-4444-444444444444" => Json($$"""
                    {"license":{{License}},"activations":[{"id":"55555555-5555-5555-5555-555555555555","licenseId":"44444444-4444-4444-4444-444444444444","deviceId":"dev-1","deviceName":"PC","appVersion":"2.4","status":"Deactivated","activatedAt":"2026-10-01T00:00:00Z","deactivatedAt":null,"lastHeartbeatAt":null,"lastIpAddress":null,"operatingSystem":"Windows","online":false}]}
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        }

        private const string License = """{"id":"44444444-4444-4444-4444-444444444444","tenantId":"00000000-0000-0000-0000-000000000001","customerId":"66666666-6666-6666-6666-666666666666","customerName":"Acme","subscriptionId":"77777777-7777-7777-7777-777777777777","productCode":"UBG","planCode":"STD","planVersion":2,"licenseNumber":"LIC-00042","productKeyPrefix":"UBG-FULL","status":"Active","statusReason":null,"issuedAt":"2026-10-01T00:00:00Z","expiresAt":"2027-10-01T00:00:00Z","maxActivations":3,"activeActivations":0,"features":[],"heartbeatIntervalHours":24,"offlineGraceDays":7}""";

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static (ILicensingPlatform Client, ScriptedPlatform Platform) Create()
    {
        var platform = new ScriptedPlatform();
        var options = new LicensingPlatformOptions { BaseUrl = "https://licensing.test", ServiceEmail = "site@example.test", ServicePassword = "secret" };
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PlatformTokens>();
        services.AddTransient<LicensingPlatformAuthHandler>();
        services.AddHttpClient(LicensingPlatformClient.HttpClientName, c => c.BaseAddress = new Uri("https://licensing.test/"))
            .AddHttpMessageHandler<LicensingPlatformAuthHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => platform);
        services.AddSingleton<ILicensingPlatform, LicensingPlatformClient>();
        return (services.BuildServiceProvider().GetRequiredService<ILicensingPlatform>(), platform);
    }

    [Fact]
    public async Task Each_paid_plan_is_offered_at_its_newest_published_version()
    {
        var (client, platform) = Create();
        var plans = await client.PublishedPaidPlansAsync(default);

        Assert.Equal([("PRO", 1, 2500m), ("STD", 2, 1200m)], plans.Select(p => (p.Code, p.Version, p.Price)).OrderBy(p => p.Code));
        Assert.Contains(platform.Calls, c => c.Path.StartsWith("/api/v1/plans?status=Published&", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_response_it_cannot_read_surfaces_as_unavailable()
    {
        var (client, platform) = Create();
        platform.Override = r => r.RequestUri!.AbsolutePath == "/api/v1/licenses/44444444-4444-4444-4444-444444444444"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                """{"license":{"id":"44444444-4444-4444-4444-444444444444","status":"Frozen"},"activations":[]}""", Encoding.UTF8, "application/json") }
            : null;

        await Assert.ThrowsAsync<PlatformUnavailableException>(() => client.LicenseAsync(Guid.Parse("44444444-4444-4444-4444-444444444444"), default));
    }

    [Fact]
    public async Task It_signs_in_once_and_sends_the_token_on_every_call()
    {
        var (client, platform) = Create();
        await client.PublishedPaidPlansAsync(default);
        await client.LicenseAsync(Guid.Parse("44444444-4444-4444-4444-444444444444"), default);

        Assert.Equal(1, platform.Logins);
        Assert.All(platform.Calls.Where(c => !c.Path.EndsWith("/login", StringComparison.Ordinal)), c => Assert.Equal("token-1", c.Bearer));
    }

    [Fact]
    public async Task A_refused_token_triggers_one_new_sign_in_and_a_retry()
    {
        var (client, platform) = Create();
        await client.PublishedPaidPlansAsync(default);
        var refusedOnce = false;
        platform.Override = r => r.RequestUri!.AbsolutePath == "/api/v1/plans" && !refusedOnce && (refusedOnce = true)
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : null;

        Assert.Equal(2, (await client.PublishedPaidPlansAsync(default)).Count);
        Assert.Equal(2, platform.Logins);
    }

    [Fact]
    public async Task Issuing_maps_the_license_and_the_one_time_key()
    {
        var (client, _) = Create();
        var issued = await client.IssueLicenseAsync(Guid.NewGuid(), default);

        Assert.Equal("UBG-FULL-KEY", issued.ProductKey);
        Assert.Equal(("LIC-00042", "Active"), (issued.License.LicenseNumber, issued.License.Status));
    }

    [Fact]
    public async Task Devices_are_mapped_and_a_missing_license_is_null()
    {
        var (client, _) = Create();
        var found = await client.LicenseAsync(Guid.Parse("44444444-4444-4444-4444-444444444444"), default);

        Assert.Equal("Deactivated", Assert.Single(found!.Value.Devices).Status);
        Assert.Null(await client.LicenseAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Server_errors_and_bad_credentials_surface_as_unavailable()
    {
        var (client, platform) = Create();
        platform.Override = r => r.RequestUri!.AbsolutePath == "/api/v1/plans" ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null;
        await Assert.ThrowsAsync<PlatformUnavailableException>(() => client.PublishedPaidPlansAsync(default));

        var (other, otherPlatform) = Create();
        otherPlatform.Override = r => r.RequestUri!.AbsolutePath == "/api/v1/auth/login" ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : null;
        await Assert.ThrowsAsync<PlatformUnavailableException>(() => other.PublishedPaidPlansAsync(default));
    }
}
