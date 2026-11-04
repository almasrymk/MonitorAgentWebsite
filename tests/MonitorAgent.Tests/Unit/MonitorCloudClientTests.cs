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
        public int Logins { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter));
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
                return Task.FromResult(Json($$"""{"accessToken":"token-{{++Logins}}","expiresAt":"2999-01-01T00:00:00Z","refreshToken":"r","user":null}"""));
            if (path == "/api/v1/platform/tenants/registrations" && request.Headers.Authorization?.Parameter == "token-1")
                return Task.FromResult(Json("""{"tenantId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","created":true,"status":"Active"}"""));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task Registration_signs_in_once_and_returns_the_tenant()
    {
        var cloud = new ScriptedCloud();
        var options = new MonitorCloudOptions { BaseUrl = "https://cloud.test", ServiceEmail = "site@example.test", ServicePassword = "secret" };
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<MonitorTokens>();
        services.AddTransient<MonitorCloudAuthHandler>();
        services.AddHttpClient(MonitorCloudClient.HttpClientName, c => c.BaseAddress = new Uri("https://cloud.test/"))
            .AddHttpMessageHandler<MonitorCloudAuthHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => cloud);
        services.AddSingleton<IMonitorCloud, MonitorCloudClient>();
        var client = services.BuildServiceProvider().GetRequiredService<IMonitorCloud>();

        var first = await client.RegisterTenantAsync(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Acme", default);
        var second = await client.RegisterTenantAsync(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Acme", default);

        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), first);
        Assert.Equal(first, second);
        Assert.Equal(1, cloud.Logins);
        Assert.Equal(2, cloud.Calls.Count(c => c.Path == "/api/v1/platform/tenants/registrations" && c.Bearer == "token-1"));
    }
}
