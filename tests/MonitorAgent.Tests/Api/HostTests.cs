using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace MonitorAgent.Tests.Api;

public class HostTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_answer(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Every_response_carries_the_security_headers()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        var response = await _client.GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OpenApi_document_is_served_in_development()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("openapi", out _));
    }

    [Fact]
    public async Task Cors_allows_only_the_configured_web_origin()
    {
        using var allowed = new HttpRequestMessage(HttpMethod.Options, "/health/live");
        allowed.Headers.Add("Origin", "http://localhost:4200");
        allowed.Headers.Add("Access-Control-Request-Method", "GET");
        using var denied = new HttpRequestMessage(HttpMethod.Options, "/health/live");
        denied.Headers.Add("Origin", "https://evil.example");
        denied.Headers.Add("Access-Control-Request-Method", "GET");

        var ok = await _client.SendAsync(allowed);
        var blocked = await _client.SendAsync(denied);

        Assert.Equal("http://localhost:4200", ok.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(blocked.Headers.Contains("Access-Control-Allow-Origin"));
    }
}

/// <summary>An API behind a reverse proxy on 10.0.0.0/8 that allows two sign-in attempts per minute per visitor.</summary>
public sealed class BehindProxyFactory : ApiFactory
{
    protected override IDictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["ReverseProxy:KnownNetworks:0"] = "10.0.0.0/8";
            settings["RateLimiting:AuthPermitPerMinute"] = "2";
            return settings;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // TestServer has no socket; every request arrives as if from the proxy at 10.0.0.5.
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, FromProxy>());
    }

    private sealed class FromProxy : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((http, nextMiddleware) =>
            {
                http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.5");
                return nextMiddleware(http);
            });
            next(app);
        };
    }
}

public class BehindProxyTests(BehindProxyFactory factory) : IClassFixture<BehindProxyFactory>
{
    private async Task<HttpStatusCode> LoginFromAsync(string? visitor)
    {
        var client = factory.CreateTestClient();
        if (visitor is not null) client.Http.DefaultRequestHeaders.Add("X-Forwarded-For", visitor);
        return (await client.LoginAsync("nobody@example.test", "not a real password")).StatusCode;
    }

    [Fact]
    public async Task Each_visitor_behind_the_proxy_gets_their_own_sign_in_budget()
    {
        for (var i = 0; i < 2; i++) Assert.NotEqual(HttpStatusCode.TooManyRequests, await LoginFromAsync("203.0.113.7"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await LoginFromAsync("203.0.113.7"));

        Assert.NotEqual(HttpStatusCode.TooManyRequests, await LoginFromAsync("198.51.100.20"));
    }
}
