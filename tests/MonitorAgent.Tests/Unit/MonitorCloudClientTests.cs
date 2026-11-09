using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.Modules.Licenses.Application;
using MonitorAgent.Modules.Licenses.Infrastructure.Platform;

namespace MonitorAgent.Tests.Unit;

/// <summary>The real HTTP client against a scripted Monitor Cloud, using its login and tenant registration responses.</summary>
public class MonitorCloudClientTests
{
    private sealed class ScriptedCloud : HttpMessageHandler
    {
        public List<(string Method, string Path, string? Bearer)> Calls { get; } = [];
        public List<string> Bodies { get; } = [];
        public int Logins { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter));
            Bodies.Add(request.Content is null ? "" : request.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult());
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/client-token")
                return Task.FromResult(Json($$"""{"accessToken":"token-{{++Logins}}","expiresAt":"2999-01-01T00:00:00Z","tokenType":"Bearer","scopes":["tenants.provision"]}"""));
            if (path == "/api/v1/auth/login")
                return Task.FromResult(Json($$"""{"accessToken":"token-{{++Logins}}","expiresAt":"2999-01-01T00:00:00Z","refreshToken":"r","user":null}"""));
            if (path == "/api/v1/platform/tenants/registrations" && request.Headers.Authorization?.Parameter == "token-1")
                return Task.FromResult(Json("""{"tenantId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","created":true,"status":"Active"}"""));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static IMonitorCloud Client(ScriptedCloud cloud, MonitorCloudOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<MonitorTokens>();
        services.AddTransient<MonitorCloudAuthHandler>();
        services.AddHttpClient(MonitorCloudClient.HttpClientName, c => c.BaseAddress = new Uri("https://cloud.test/"))
            .AddHttpMessageHandler<MonitorCloudAuthHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => cloud);
        services.AddSingleton<IMonitorCloud, MonitorCloudClient>();
        return services.BuildServiceProvider().GetRequiredService<IMonitorCloud>();
    }

    [Fact]
    public async Task Registration_with_the_service_client_signs_in_once_and_sends_the_owner()
    {
        var cloud = new ScriptedCloud();
        var client = Client(cloud, new MonitorCloudOptions { BaseUrl = "https://cloud.test", ClientId = "mcs_test", ClientSecret = "TEST-ONLY-secret" });
        var owner = new MonitorTenantOwner("owner@acme.example", "Acme Owner");

        var first = await client.RegisterTenantAsync(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Acme", owner, default);
        var second = await client.RegisterTenantAsync(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Acme", owner, default);

        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), first);
        Assert.Equal(first, second);
        Assert.Equal(1, cloud.Logins);
        Assert.DoesNotContain(cloud.Calls, c => c.Path == "/api/v1/auth/login");
        Assert.Contains("\"clientId\":\"mcs_test\"", cloud.Bodies[0], StringComparison.Ordinal);
        Assert.Equal(2, cloud.Calls.Count(c => c.Path == "/api/v1/platform/tenants/registrations" && c.Bearer == "token-1"));
        Assert.Contains("\"ownerEmail\":\"owner@acme.example\"", cloud.Bodies[1], StringComparison.Ordinal);
        Assert.Contains("\"ownerName\":\"Acme Owner\"", cloud.Bodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_client_id_the_deprecated_service_user_still_signs_in()
    {
        var cloud = new ScriptedCloud();
        var client = Client(cloud, new MonitorCloudOptions { BaseUrl = "https://cloud.test", ServiceEmail = "site@example.test", ServicePassword = "TEST-ONLY-password" });

        var tenant = await client.RegisterTenantAsync(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Acme", null, default);

        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), tenant);
        Assert.Equal("/api/v1/auth/login", cloud.Calls[0].Path);
    }
}
