using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.Modules.Identity.Infrastructure.Persistence;

namespace MonitorAgent.Tests.Api;

public class IdentityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task A_new_customer_registers_confirms_the_email_and_signs_in()
    {
        var client = factory.CreateTestClient();
        var email = NewEmail();
        var password = await client.RegisterConfirmedAsync(email);

        await client.SignInAsync(email, password);
        var me = await JsonAsync(await client.GetAsync("/api/v1/account/me"));

        Assert.Equal(email, me.GetProperty("email").GetString());
        Assert.True(me.GetProperty("emailConfirmed").GetBoolean());
        Assert.Equal(["Customer"], me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Registering_an_existing_email_answers_the_same_and_only_notifies_the_owner()
    {
        var client = factory.CreateTestClient();
        var email = NewEmail();
        await client.RegisterConfirmedAsync(email);

        var again = await client.PostAsync("/api/v1/auth/register", new { email, password = "another long password", fullName = "Someone" });

        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Contains("already have one", factory.Emails.To(email)[^1].TextBody, StringComparison.Ordinal);
        await client.SignInAsync(email, "correct horse battery");
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_give_the_same_answer()
    {
        var client = factory.CreateTestClient();
        var email = NewEmail();
        await client.RegisterConfirmedAsync(email);

        var unknown = await client.LoginAsync(NewEmail(), "correct horse battery");
        var wrong = await client.LoginAsync(email, "not the password at all");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await TestClient.ErrorCodeAsync(unknown));
        Assert.Equal("INVALID_CREDENTIALS", await TestClient.ErrorCodeAsync(wrong));
    }

    [Fact]
    public async Task An_unconfirmed_account_is_only_revealed_to_someone_with_its_password()
    {
        var client = factory.CreateTestClient();
        var email = NewEmail();
        await client.PostAsync("/api/v1/auth/register", new { email, password = "correct horse battery", fullName = "Test" });

        Assert.Equal("INVALID_CREDENTIALS", await TestClient.ErrorCodeAsync(await client.LoginAsync(email, "wrong password here")));
        Assert.Equal("EMAIL_NOT_CONFIRMED", await TestClient.ErrorCodeAsync(await client.LoginAsync(email, "correct horse battery")));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_one()
    {
        var client = factory.CreateTestClient();
        var email = NewEmail();
        var password = await client.RegisterConfirmedAsync(email);

        for (var i = 0; i < 5; i++) await client.LoginAsync(email, "wrong password " + i);
        var locked = await client.LoginAsync(email, password);

        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
        Assert.Equal("ACCOUNT_LOCKED", await TestClient.ErrorCodeAsync(locked));
    }

    [Fact]
    public async Task Short_passwords_are_rejected()
    {
        var client = factory.CreateTestClient();
        var response = await client.PostAsync("/api/v1/auth/register", new { email = NewEmail(), password = "short-pass1", fullName = "Test" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await TestClient.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Writes_without_the_csrf_header_are_rejected()
    {
        var client = factory.CreateTestClient();
        var email = NewEmail();
        var password = await client.RegisterConfirmedAsync(email);

        var response = await client.PostAsync("/api/v1/auth/login", new { email, password }, withCsrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], c => c.StartsWith("__Host-ma.session", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_a_session_account_routes_answer_401_problem_not_a_redirect()
    {
        var response = await factory.CreateTestClient().GetAsync("/api/v1/account/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("UNAUTHENTICATED", await TestClient.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task The_session_cookie_is_host_only_secure_httponly_and_strict()
    {
        var client = factory.CreateTestClient();
        var email = NewEmail();
        var password = await client.RegisterConfirmedAsync(email);

        var response = await client.PostAsync("/api/v1/auth/login", new { email, password });
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-ma.session=", StringComparison.Ordinal)).ToLowerInvariant();

        Assert.Contains("secure", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.Ordinal);
        Assert.Contains("samesite=strict", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/", cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("domain=", cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forgot_password_for_an_unknown_email_answers_the_same_and_sends_nothing()
    {
        var client = factory.CreateTestClient();
        var email = NewEmail();

        var response = await client.PostAsync("/api/v1/auth/forgot-password", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(factory.Emails.To(email));
    }

    [Fact]
    public async Task A_password_reset_works_once_and_signs_out_every_session()
    {
        var phone = factory.CreateTestClient();
        var laptop = factory.CreateTestClient();
        var email = NewEmail();
        var password = await phone.RegisterConfirmedAsync(email);
        await phone.SignInAsync(email, password);

        await laptop.PostAsync("/api/v1/auth/forgot-password", new { email });
        var token = laptop.ResetToken(email);
        var reset = await laptop.PostAsync("/api/v1/auth/reset-password", new { email, token, newPassword = "a brand new passphrase" });
        var reused = await laptop.PostAsync("/api/v1/auth/reset-password", new { email, token, newPassword = "yet another passphrase" });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal("INVALID_TOKEN", await TestClient.ErrorCodeAsync(reused));
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/v1/account/me")).StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await TestClient.ErrorCodeAsync(await laptop.LoginAsync(email, password)));
        await laptop.SignInAsync(email, "a brand new passphrase");
    }

    [Fact]
    public async Task Changing_the_password_keeps_this_session_and_ends_the_others()
    {
        var phone = factory.CreateTestClient();
        var laptop = factory.CreateTestClient();
        var email = NewEmail();
        var password = await phone.RegisterConfirmedAsync(email);
        await phone.SignInAsync(email, password);
        await laptop.SignInAsync(email, password);

        var wrong = await laptop.PostAsync("/api/v1/account/change-password", new { currentPassword = "not it at all", newPassword = "changed passphrase here" });
        var changed = await laptop.PostAsync("/api/v1/account/change-password", new { currentPassword = password, newPassword = "changed passphrase here" });

        Assert.Equal("WRONG_PASSWORD", await TestClient.ErrorCodeAsync(wrong));
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync("/api/v1/account/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/v1/account/me")).StatusCode);
    }

    [Fact]
    public async Task A_mail_outage_does_not_turn_a_saved_change_into_an_error()
    {
        var client = factory.CreateTestClient();
        var email = NewEmail();
        var password = await client.RegisterConfirmedAsync(email);
        await client.SignInAsync(email, password);
        factory.Emails.FailFor(email);

        var changed = await client.PostAsync("/api/v1/account/change-password", new { currentPassword = password, newPassword = "changed passphrase here" });
        var forgot = await client.PostAsync("/api/v1/auth/forgot-password", new { email });
        var other = NewEmail();
        factory.Emails.FailFor(other);
        var registered = await client.PostAsync("/api/v1/auth/register", new { email = other, password = "another long password", fullName = "Someone" });

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.True(forgot.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDb>();
        var userId = db.Users.Single(u => u.Email == email).Id;
        var types = db.SecurityEvents.Where(e => e.UserId == userId).Select(e => e.Type.ToString()).ToList();
        Assert.Contains("PasswordChanged", types);
        Assert.Contains("PasswordResetRequested", types);
        Assert.Single(db.Users.Where(u => u.Email == other));
    }

    [Fact]
    public async Task Sign_out_everywhere_ends_every_session()
    {
        var phone = factory.CreateTestClient();
        var laptop = factory.CreateTestClient();
        var email = NewEmail();
        var password = await phone.RegisterConfirmedAsync(email);
        await phone.SignInAsync(email, password);
        await laptop.SignInAsync(email, password);

        Assert.Equal(HttpStatusCode.NoContent, (await laptop.PostAsync("/api/v1/account/sessions/revoke")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/v1/account/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await laptop.GetAsync("/api/v1/account/me")).StatusCode);
    }

    [Fact]
    public async Task With_two_step_verification_on_sign_in_needs_a_code_and_a_recovery_code_works_once()
    {
        var client = factory.CreateTestClient();
        var email = NewEmail();
        var password = await client.RegisterConfirmedAsync(email);
        await client.SignInAsync(email, password);
        var codes = await EnableMfaAsync(client);
        var key = await SharedKeyOf(email);
        await client.LogoutAsync();

        var login = await client.LoginAsync(email, password);
        Assert.True((await JsonAsync(login)).GetProperty("requiresMfa").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/account/me")).StatusCode);
        Assert.Equal("INVALID_MFA_CODE", await TestClient.ErrorCodeAsync(await client.PostAsync("/api/v1/auth/login/mfa", new { code = "000000" })));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/login/mfa", new { code = TestClient.Totp(key) })).StatusCode);
        await client.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/account/me")).StatusCode);

        await client.LogoutAsync();
        await client.LoginAsync(email, password);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/login/recovery-code", new { code = codes[0] })).StatusCode);
        await client.RefreshCsrfAsync();
        await client.LogoutAsync();
        await client.LoginAsync(email, password);
        Assert.Equal("INVALID_MFA_CODE",
            await TestClient.ErrorCodeAsync(await client.PostAsync("/api/v1/auth/login/recovery-code", new { code = codes[0] })));
    }

    [Fact]
    public async Task A_code_without_a_pending_sign_in_is_refused()
    {
        var response = await factory.CreateTestClient().PostAsync("/api/v1/auth/login/mfa", new { code = "123456" });

        Assert.Equal("MFA_SESSION_EXPIRED", await TestClient.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Customers_cannot_reach_the_admin_area()
    {
        var client = factory.CreateTestClient();
        var email = NewEmail();
        await client.SignInAsync(email, await client.RegisterConfirmedAsync(email));

        var response = await client.GetAsync("/api/v1/admin/security-events");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", await TestClient.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Staff_need_two_step_verification_for_the_admin_area_and_cannot_turn_it_off()
    {
        var admin = factory.CreateTestClient();
        await admin.SignInAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        var before = await admin.GetAsync("/api/v1/admin/security-events");
        Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);
        Assert.Equal("MFA_REQUIRED", await TestClient.ErrorCodeAsync(before));

        await EnableMfaAsync(admin);
        var after = await admin.GetAsync("/api/v1/admin/security-events");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Contains((await JsonAsync(after)).GetProperty("items").EnumerateArray(), e => e.GetProperty("type").GetString() == "MfaEnabled");

        var disable = await admin.PostAsync("/api/v1/account/mfa/disable", new { password = ApiFactory.AdminPassword });
        Assert.Equal("MFA_REQUIRED_FOR_STAFF", await TestClient.ErrorCodeAsync(disable));
    }

    [Fact]
    public async Task The_security_log_records_sign_ins_and_failures_without_secrets()
    {
        var client = factory.CreateTestClient();
        var email = NewEmail();
        var password = await client.RegisterConfirmedAsync(email);
        await client.LoginAsync(email, "a wrong password");
        await client.SignInAsync(email, password);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDb>();
        var userId = db.Users.Single(u => u.Email == email).Id;
        var types = db.SecurityEvents.Where(e => e.UserId == userId).AsEnumerable().OrderBy(e => e.OccurredAt).Select(e => e.Type.ToString()).ToList();

        Assert.Equal(["Registered", "EmailConfirmed", "SignInFailed", "SignedIn"], types);
    }

    private async Task<List<string>> EnableMfaAsync(TestClient client)
    {
        var setup = await JsonAsync(await client.GetAsync("/api/v1/account/mfa/setup"));
        var key = setup.GetProperty("sharedKey").GetString()!;
        Assert.StartsWith("otpauth://totp/", setup.GetProperty("authenticatorUri").GetString(), StringComparison.Ordinal);

        Assert.Equal("INVALID_MFA_CODE", await TestClient.ErrorCodeAsync(await client.PostAsync("/api/v1/account/mfa/enable", new { code = "000000" })));
        var enabled = await client.PostAsync("/api/v1/account/mfa/enable", new { code = TestClient.Totp(key) });
        Assert.True(enabled.StatusCode == HttpStatusCode.OK, await enabled.Content.ReadAsStringAsync());
        await client.RefreshCsrfAsync();
        return (await enabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToList();
    }

    private async Task<string> SharedKeyOf(string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        return (await users.GetAuthenticatorKeyAsync((await users.FindByEmailAsync(email))!))!;
    }
}

public sealed class StrictRateLimitFactory : ApiFactory
{
    protected override IDictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["RateLimiting:AuthPermitPerMinute"] = "3";
            return settings;
        }
    }
}

public class AuthRateLimitTests(StrictRateLimitFactory factory) : IClassFixture<StrictRateLimitFactory>
{
    [Fact]
    public async Task Repeated_sign_in_attempts_are_throttled()
    {
        var client = factory.CreateTestClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            statuses.Add((await client.PostAsync("/api/v1/auth/login", new { email = "nobody@example.test", password = "whatever password" })).StatusCode);

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized], statuses.Take(3));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }
}
