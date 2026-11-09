using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MonitorAgent.Modules.Licenses.Application;

namespace MonitorAgent.Modules.Licenses.Infrastructure.Platform;

public sealed class MonitorCloudOptions
{
    /// <summary>Monitor Cloud's base URL, e.g. https://cloud.example.com.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// The website's Monitor Cloud service client (Cloud ADR 0023, INT-030), created on the Cloud server with
    /// <c>MonitorCloud.Api service-client create website --scope tenants.provision</c>. It can only register customers.
    /// The secret comes from a secret store, never the repo.
    /// </summary>
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Deprecated (INT-030): a Monitor Cloud Platform Admin user and password. Used only while <see cref="ClientId"/> is
    /// empty; remove both after switching, then deactivate that user in Monitor Cloud.
    /// </summary>
    public string ServiceEmail { get; set; } = "";
    public string ServicePassword { get; set; } = "";

    public bool UsesClient => !string.IsNullOrWhiteSpace(ClientId);

    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>
/// Registers the website's Licensing customer as a Monitor Cloud tenant and names the company owner, whom Monitor Cloud
/// invites once as the tenant's first Administrator (Cloud ADR 0018). Older Cloud builds ignore the owner fields.
/// </summary>
internal sealed class MonitorCloudClient(IHttpClientFactory http) : IMonitorCloud
{
    public const string HttpClientName = "MonitorCloud";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web);

    public async Task<Guid> RegisterTenantAsync(Guid licensingCustomerId, string name, MonitorTenantOwner? owner, CancellationToken ct)
    {
        var registered = await SendAsync<RegisteredBody>(HttpMethod.Post, "api/v1/platform/tenants/registrations",
            new { licensingCustomerId, name, ownerEmail = owner?.Email, ownerName = owner?.FullName }, ct);
        if (registered.TenantId == Guid.Empty)
            throw new MonitorUnavailableException("Monitor Cloud sent a registration without a tenant id.");
        return registered.TenantId;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object body, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, path, body, ct);
        await EnsureAsync(response);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct)
                ?? throw new MonitorUnavailableException("Monitor Cloud sent an empty response.");
        }
        catch (JsonException ex)
        {
            throw new MonitorUnavailableException("Monitor Cloud sent a response this website can't read.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        try
        {
            return await http.CreateClient(HttpClientName).SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new MonitorUnavailableException("Monitor Cloud is unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MonitorUnavailableException("Monitor Cloud timed out.", ex);
        }
    }

    private static async Task EnsureAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string? code = null;
        try
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            code = problem.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
        }
        catch (JsonException) { }
        throw new MonitorUnavailableException($"Monitor Cloud answered {(int)response.StatusCode} {code}.");
    }

    private sealed record RegisteredBody(Guid TenantId, bool Created, string? Status, string? OwnerInvitation);
}

/// <summary>The service account's current Monitor Cloud token, shared by every request.</summary>
internal sealed class MonitorTokens
{
    public SemaphoreSlim Lock { get; } = new(1, 1);
    public string? Token { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Signs in to Monitor Cloud with the website's service client (or, deprecated, its service user) and adds the bearer token to every call. The token is
/// reused until a minute before it expires, and a 401 triggers one fresh sign-in and retry.
/// </summary>
internal sealed class MonitorCloudAuthHandler(MonitorCloudOptions options, MonitorTokens tokens, TimeProvider clock) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await TokenAsync(request.RequestUri!, stale: null, ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(request.RequestUri!, stale: token, ct));
        return await base.SendAsync(request, ct);
    }

    private async Task<string> TokenAsync(Uri requestUri, string? stale, CancellationToken ct)
    {
        await tokens.Lock.WaitAsync(ct);
        try
        {
            if (tokens.Token is not null && tokens.Token != stale && clock.GetUtcNow() < tokens.ExpiresAt - TimeSpan.FromMinutes(1))
                return tokens.Token;

            var baseUri = new Uri(string.IsNullOrWhiteSpace(options.BaseUrl)
                ? requestUri.GetLeftPart(UriPartial.Authority) + "/" : options.BaseUrl.TrimEnd('/') + "/");
            using var login = options.UsesClient
                ? new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/v1/auth/client-token"))
                {
                    Content = JsonContent.Create(new { clientId = options.ClientId, clientSecret = options.ClientSecret }),
                }
                : new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/v1/auth/login"))
                {
                    Content = JsonContent.Create(new { email = options.ServiceEmail, password = options.ServicePassword }),
                };
            using var response = await base.SendAsync(login, ct);
            if (!response.IsSuccessStatusCode)
                throw new MonitorUnavailableException($"Monitor Cloud rejected the website's {(options.UsesClient ? "service client" : "service account")} ({(int)response.StatusCode}).");
            var auth = await response.Content.ReadFromJsonAsync<AuthBody>(JsonSerializerOptions.Web, ct)
                ?? throw new MonitorUnavailableException("Monitor Cloud sent an empty sign-in response.");
            (tokens.Token, tokens.ExpiresAt) = (auth.AccessToken, auth.ExpiresAt);
            return auth.AccessToken;
        }
        finally
        {
            tokens.Lock.Release();
        }
    }

    private sealed record AuthBody(string AccessToken, DateTimeOffset ExpiresAt);
}
