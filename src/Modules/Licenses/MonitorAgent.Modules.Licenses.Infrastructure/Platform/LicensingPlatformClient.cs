using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MonitorAgent.Modules.Licenses.Application;

namespace MonitorAgent.Modules.Licenses.Infrastructure.Platform;

public sealed class LicensingPlatformOptions
{
    /// <summary>LicensingPlatform's base URL, e.g. https://licensing.example.com.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// A dedicated LicensingPlatform user for the website (ADR-007). Its role needs catalog.read, customers.manage,
    /// subscriptions.manage and licenses.read/manage. The password comes from a secret store, never the repo.
    /// </summary>
    public string ServiceEmail { get; set; } = "";
    public string ServicePassword { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>Talks to LicensingPlatform's v1 API and maps its shapes to this module's. Enum values are LicensingPlatform's.</summary>
internal sealed class LicensingPlatformClient(IHttpClientFactory http) : ILicensingPlatform
{
    public const string HttpClientName = "LicensingPlatform";

    // LicensingPlatform writes its enums as names ("Published", "Paid"); numbers are still accepted.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web) { Converters = { new JsonStringEnumConverter() } };

    // Mirrors of LicensingPlatform's enums (Licensing.Domain), with its numeric values.
    private enum PlanStatus { Draft = 1, Published = 2, Archived = 3 }
    private enum PlanKind { Paid = 1, Free = 3 }
    private enum LicenseStatus { Active = 1, Suspended = 2, Revoked = 3, Expired = 4 }
    private enum ActivationStatus { Active = 1, Deactivated = 2 }

    /// <summary>
    /// A published plan never changes; a price change is published as a new version of the same code and the old one may
    /// stay published for existing licenses. So each plan code is offered at its newest published version.
    /// (LicensingPlatform's IsCurrent marks the free offer only, so it says nothing about paid plans.)
    /// </summary>
    public async Task<IReadOnlyList<PlatformPlan>> PublishedPaidPlansAsync(CancellationToken ct)
    {
        var page = await GetAsync<Paged<PlanBody>>($"api/v1/plans?status={PlanStatus.Published}&page=1&pageSize=200", ct);
        return page!.Items
            .Where(p => p.Kind == PlanKind.Paid && p.Status == PlanStatus.Published)
            .GroupBy(p => (p.ProductCode, p.Code))
            .Select(versions => versions.MaxBy(p => p.Version)!)
            .Select(p => new PlatformPlan(p.Id, p.ProductCode, p.ProductName, p.Code, p.Name, p.Version, p.Price, p.Currency,
                p.DurationDays, p.MaxActivations, p.Features ?? []))
            .ToList();
    }

    public async Task<Guid> CreateCustomerAsync(string name, string email, CancellationToken ct)
    {
        var created = await SendAsync<CustomerDetailsBody>(HttpMethod.Post, "api/v1/customers",
            new { name, email = string.IsNullOrEmpty(email) ? null : email }, ct);
        return created.Customer.Id;
    }

    public async Task<Guid> StartSubscriptionAsync(Guid customerId, Guid planId, CancellationToken ct)
    {
        var started = await SendAsync<SubscriptionDetailsBody>(HttpMethod.Post, "api/v1/subscriptions",
            new { customerId, planId, notes = "Website order" }, ct);
        return started.Subscription.Id;
    }

    public async Task<PlatformIssuedLicense> IssueLicenseAsync(Guid subscriptionId, CancellationToken ct)
    {
        var issued = await SendAsync<IssuedLicenseBody>(HttpMethod.Post, "api/v1/licenses", new { subscriptionId }, ct);
        return new PlatformIssuedLicense(ToLicense(issued.License), issued.ProductKey);
    }

    public async Task<IReadOnlyList<PlatformLicense>> LicensesOfAsync(Guid customerId, CancellationToken ct)
    {
        var page = await GetAsync<Paged<LicenseBody>>($"api/v1/licenses?customerId={customerId}&page=1&pageSize=200", ct);
        return page!.Items.Select(ToLicense).ToList();
    }

    public async Task<(PlatformLicense License, IReadOnlyList<PlatformDevice> Devices)?> LicenseAsync(Guid licenseId, CancellationToken ct)
    {
        var details = await GetAsync<LicenseDetailsBody>($"api/v1/licenses/{licenseId}", ct);
        if (details is null) return null;
        return (ToLicense(details.License), details.Activations.Select(a => new PlatformDevice(a.Id, a.DeviceId, a.DeviceName,
            a.OperatingSystem, a.AppVersion, a.Status.ToString(), a.ActivatedAt, a.LastHeartbeatAt, a.Online)).ToList());
    }

    public async Task ResetDeviceAsync(Guid licenseId, Guid deviceId, CancellationToken ct)
    {
        using var response = await SendRawAsync(HttpMethod.Post, $"api/v1/licenses/{licenseId}/activations/{deviceId}/reset", null, ct);
        await EnsureAsync(response);
    }

    private static PlatformLicense ToLicense(LicenseBody l) => new(l.Id, l.CustomerId, l.ProductCode, l.PlanCode, l.LicenseNumber,
        l.ProductKeyPrefix, l.Status.ToString(), l.IssuedAt, l.ExpiresAt, l.MaxActivations, l.ActiveActivations);

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        using var response = await SendRawAsync(HttpMethod.Get, path, null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureAsync(response);
        return await ReadAsync<T>(response, ct);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object body, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, path, body, ct);
        await EnsureAsync(response);
        return (await ReadAsync<T>(response, ct))!;
    }

    /// <summary>A body this client can't read (a renamed field, a new enum value) is treated like any other failure.</summary>
    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct);
        }
        catch (JsonException ex)
        {
            throw new PlatformUnavailableException("LicensingPlatform sent a response this website can't read.", ex);
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
            throw new PlatformUnavailableException("LicensingPlatform is unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new PlatformUnavailableException("LicensingPlatform timed out.", ex);
        }
    }

    /// <summary>
    /// Any failure here is treated as "unavailable": a 4xx from LicensingPlatform means our request or its setup is wrong
    /// (for example the service account lost a permission), which the customer can't fix either.
    /// </summary>
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
        throw new PlatformUnavailableException($"LicensingPlatform answered {(int)response.StatusCode} {code}.");
    }

    private sealed record Paged<T>(IReadOnlyList<T> Items, int Total);
    private sealed record PlanBody(Guid Id, string ProductCode, string ProductName, string Code, string Name, int Version, PlanStatus Status,
        decimal Price, string Currency, int? DurationDays, int? MaxActivations, IReadOnlyList<string>? Features, PlanKind Kind);
    private sealed record IdBody(Guid Id);
    private sealed record CustomerDetailsBody(IdBody Customer);
    private sealed record SubscriptionDetailsBody(IdBody Subscription);
    private sealed record LicenseBody(Guid Id, Guid CustomerId, string ProductCode, string PlanCode, string LicenseNumber,
        string ProductKeyPrefix, LicenseStatus Status, DateTimeOffset IssuedAt, DateTimeOffset? ExpiresAt, int? MaxActivations, int ActiveActivations);
    private sealed record ActivationBody(Guid Id, string DeviceId, string? DeviceName, string? AppVersion, ActivationStatus Status,
        DateTimeOffset ActivatedAt, DateTimeOffset? LastHeartbeatAt, string? OperatingSystem, bool Online);
    private sealed record LicenseDetailsBody(LicenseBody License, IReadOnlyList<ActivationBody> Activations);
    private sealed record IssuedLicenseBody(LicenseBody License, string ProductKey);
}

/// <summary>The service account's current token, shared by every request (handlers themselves are recycled).</summary>
internal sealed class PlatformTokens
{
    public SemaphoreSlim Lock { get; } = new(1, 1);
    public string? Token { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Signs in to LicensingPlatform as the website's service user and adds the bearer token to every call. The token is
/// reused until a minute before it expires, and a 401 triggers one fresh sign-in and retry.
/// </summary>
internal sealed class LicensingPlatformAuthHandler(LicensingPlatformOptions options, PlatformTokens tokens, TimeProvider clock) : DelegatingHandler
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

    /// <param name="stale">A token that was just refused; a new one is fetched unless another request already replaced it.</param>
    private async Task<string> TokenAsync(Uri requestUri, string? stale, CancellationToken ct)
    {
        await tokens.Lock.WaitAsync(ct);
        try
        {
            if (tokens.Token is not null && tokens.Token != stale && clock.GetUtcNow() < tokens.ExpiresAt - TimeSpan.FromMinutes(1))
                return tokens.Token;

            // Handlers see absolute URIs only; the login call resolves against the same base as the API calls.
            var baseUri = new Uri(string.IsNullOrWhiteSpace(options.BaseUrl)
                ? requestUri.GetLeftPart(UriPartial.Authority) + "/" : options.BaseUrl.TrimEnd('/') + "/");
            using var login = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/v1/auth/login"))
            {
                Content = JsonContent.Create(new { email = options.ServiceEmail, password = options.ServicePassword }),
            };
            using var response = await base.SendAsync(login, ct);
            if (!response.IsSuccessStatusCode)
                throw new PlatformUnavailableException($"LicensingPlatform rejected the website's service account ({(int)response.StatusCode}).");
            var auth = await response.Content.ReadFromJsonAsync<AuthBody>(JsonSerializerOptions.Web, ct)
                ?? throw new PlatformUnavailableException("LicensingPlatform sent an empty sign-in response.");
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
