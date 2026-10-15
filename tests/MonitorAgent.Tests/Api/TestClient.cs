using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace MonitorAgent.Tests.Api;

/// <summary>A browser-like client: keeps cookies and sends the CSRF header the way the Angular apps do.</summary>
public sealed partial class TestClient(HttpClient http, CapturedEmails emails)
{
    private string? _csrf;

    public HttpClient Http => http;

    public async Task RefreshCsrfAsync()
    {
        var response = await http.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        _csrf = response.Headers.GetValues("Set-Cookie")
            .Select(c => XsrfCookie().Match(c))
            .First(m => m.Success).Groups[1].Value;
    }

    public async Task<HttpResponseMessage> PostAsync(string path, object? body = null, bool withCsrf = true)
    {
        if (withCsrf && _csrf is null) await RefreshCsrfAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body ?? new { }) };
        if (withCsrf) request.Headers.Add("X-XSRF-TOKEN", _csrf);
        return await http.SendAsync(request);
    }

    public async Task<HttpResponseMessage> PutAsync(string path, object body)
    {
        if (_csrf is null) await RefreshCsrfAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", _csrf);
        return await http.SendAsync(request);
    }

    public Task<HttpResponseMessage> GetAsync(string path) => http.GetAsync(path);

    /// <summary>Adds the CSRF header to a hand-built request (PUT, DELETE).</summary>
    public HttpRequestMessage WithCsrf(HttpRequestMessage request)
    {
        request.Headers.Add("X-XSRF-TOKEN", _csrf);
        return request;
    }

    /// <summary>Registers and confirms an account through the emailed link, the way a customer would.</summary>
    public async Task<string> RegisterConfirmedAsync(string email, string password = "correct horse battery")
    {
        (await PostAsync("/api/v1/auth/register", new { email, password, fullName = "Test User" })).EnsureSuccessStatusCode();
        var (userId, token) = ConfirmLink(email);
        (await PostAsync("/api/v1/auth/confirm-email", new { userId, token })).EnsureSuccessStatusCode();
        return password;
    }

    public async Task<HttpResponseMessage> LoginAsync(string email, string password)
    {
        var response = await PostAsync("/api/v1/auth/login", new { email, password });
        await RefreshCsrfAsync();
        return response;
    }

    /// <summary>Signs out and fetches a fresh CSRF token, since the old one was bound to the signed-in user.</summary>
    public async Task LogoutAsync()
    {
        (await PostAsync("/api/v1/auth/logout")).EnsureSuccessStatusCode();
        await RefreshCsrfAsync();
    }

    /// <summary>Turns on two-step verification for the signed-in account and returns its recovery codes.</summary>
    public async Task<List<string>> EnableMfaAsync()
    {
        var setup = JsonDocument.Parse(await (await GetAsync("/api/v1/account/mfa/setup")).Content.ReadAsStringAsync()).RootElement;
        var enabled = await PostAsync("/api/v1/account/mfa/enable", new { code = Totp(setup.GetProperty("sharedKey").GetString()!) });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        await RefreshCsrfAsync();
        return JsonDocument.Parse(await enabled.Content.ReadAsStringAsync()).RootElement.GetProperty("recoveryCodes")
            .EnumerateArray().Select(c => c.GetString()!).ToList();
    }

    public async Task SignInAsync(string email, string password)
    {
        var response = await LoginAsync(email, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public (Guid UserId, string Token) ConfirmLink(string email)
    {
        var link = Link(email, "/auth/confirm-email");
        return (Guid.Parse(link["userId"]!), link["token"]!);
    }

    public string ResetToken(string email) => Link(email, "/auth/reset-password")["token"]!;

    private System.Collections.Specialized.NameValueCollection Link(string email, string path)
    {
        var url = emails.To(email).Select(m => LinkPattern().Match(m.TextBody)).Last(m => m.Success && m.Value.Contains(path, StringComparison.Ordinal)).Value;
        return HttpUtility.ParseQueryString(new Uri(url).Query);
    }

    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(body) ? null : JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>RFC 6238 code for an authenticator key, as an authenticator app would show it.</summary>
    public static string Totp(string sharedKey, DateTimeOffset? at = null)
    {
        var key = Base32Decode(sharedKey.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant());
        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        var message = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(message);
#pragma warning disable CA5350 // RFC 6238 TOTP is defined over HMAC-SHA1; authenticator apps use it.
        var hash = HMACSHA1.HashData(key, message);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var output = new List<byte>();
        foreach (var c in input.TrimEnd('='))
        {
            value = (value << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(value >> (bits - 8)));
                bits -= 8;
            }
        }
        return [.. output];
    }

    [GeneratedRegex("XSRF-TOKEN=([^;]+)")]
    private static partial Regex XsrfCookie();

    [GeneratedRegex(@"https://\S+")]
    private static partial Regex LinkPattern();
}
