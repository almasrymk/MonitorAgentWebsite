using System.Net;
using System.Text.Json;

namespace MonitorAgent.Tests.Api;

public class DemoRequestTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static object Request(string email, string company = "Nile Gas Stations", string? website = null, string name = "Mona Adel") => new
    {
        name, company, email, phone = "+20 100 000 0000", sites = 50, message = "We run 50 stations and want to see the reports.", language = "ar",
        website,
    };

    private static string NewEmail() => $"lead-{Guid.NewGuid():N}@example.test";

    private async Task<JsonElement[]> ListedAsync(TestClient staff, string email) =>
        (await JsonAsync(await staff.GetAsync("/api/v1/admin/support/demo-requests?pageSize=100"))).GetProperty("items").EnumerateArray()
            .Where(r => r.GetProperty("email").GetString() == email).ToArray();

    [Fact]
    public async Task A_visitor_asks_for_a_demo_and_sales_follows_up()
    {
        var email = NewEmail();
        var visitor = factory.CreateTestClient();
        Assert.Equal(HttpStatusCode.Accepted, (await visitor.PostAsync("/api/v1/demo-requests", Request(email))).StatusCode);

        var sales = factory.Emails.To(ApiFactory.SalesEmail).Last(m => m.TextBody.Contains(email, StringComparison.Ordinal));
        Assert.Contains("Nile Gas Stations", sales.Subject, StringComparison.Ordinal);
        Assert.Contains("We run 50 stations", sales.TextBody, StringComparison.Ordinal);
        Assert.Empty(factory.Emails.To(email));

        var staff = await factory.StaffAsync("Support");
        var listed = Assert.Single(await ListedAsync(staff, email));
        Assert.Equal("New", listed.GetProperty("status").GetString());
        Assert.Equal(50, listed.GetProperty("sites").GetInt32());
        Assert.Equal("ar", listed.GetProperty("language").GetString());

        var id = listed.GetProperty("id").GetGuid();
        var handled = await JsonAsync(await staff.PutAsync($"/api/v1/admin/support/demo-requests/{id}", new { status = "Contacted", note = "Demo booked for Sunday." }));
        Assert.Equal("Contacted", handled.GetProperty("status").GetString());
        Assert.Equal("Demo booked for Sunday.", handled.GetProperty("staffNote").GetString());

        var waiting = await JsonAsync(await staff.GetAsync("/api/v1/admin/support/demo-requests?status=New&pageSize=100"));
        Assert.DoesNotContain(waiting.GetProperty("items").EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);

        var admin = await factory.StaffAsync("Admin");
        var trail = await JsonAsync(await admin.GetAsync($"/api/v1/admin/audit?target=demo-request:{id}"));
        Assert.Equal("support.handle-demo-request", Assert.Single(trail.GetProperty("items").EnumerateArray()).GetProperty("action").GetString());
    }

    [Fact]
    public async Task Bots_and_repeats_get_the_same_answer_but_add_nothing()
    {
        var visitor = factory.CreateTestClient();
        var bot = NewEmail();
        Assert.Equal(HttpStatusCode.Accepted, (await visitor.PostAsync("/api/v1/demo-requests", Request(bot, website: "https://spam.example"))).StatusCode);

        var eager = NewEmail();
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await visitor.PostAsync("/api/v1/demo-requests", Request(eager))).StatusCode);

        var staff = await factory.StaffAsync("Support");
        Assert.Empty(await ListedAsync(staff, bot));
        Assert.Single(await ListedAsync(staff, eager));
        Assert.DoesNotContain(factory.Emails.To(ApiFactory.SalesEmail), m => m.TextBody.Contains(bot, StringComparison.Ordinal));
        Assert.Single(factory.Emails.To(ApiFactory.SalesEmail), m => m.TextBody.Contains(eager, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "Acme", "a@example.test")]
    [InlineData("Mona", "", "a@example.test")]
    [InlineData("Mona", "Acme", "not an email")]
    [InlineData("Mona", "Acme\nBcc: everyone@example.test", "a@example.test")]
    public async Task Incomplete_or_unsafe_requests_are_refused(string name, string company, string email)
    {
        var response = await factory.CreateTestClient().PostAsync("/api/v1/demo-requests", Request(email, company, name: name));
        Assert.Equal("VALIDATION_FAILED", await TestClient.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task The_form_needs_the_CSRF_token_and_only_staff_see_the_requests()
    {
        var visitor = factory.CreateTestClient();
        Assert.Equal("CSRF_INVALID", await TestClient.ErrorCodeAsync(await visitor.PostAsync("/api/v1/demo-requests", Request(NewEmail()), withCsrf: false)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/v1/admin/support/demo-requests")).StatusCode);

        var customer = factory.CreateTestClient();
        var email = NewEmail();
        await customer.SignInAsync(email, await customer.RegisterConfirmedAsync(email));
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/admin/support/demo-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.StaffAsync("Finance")).GetAsync("/api/v1/admin/support/demo-requests")).StatusCode);
    }

    [Fact]
    public async Task A_demo_request_from_your_own_address_is_in_your_data_and_goes_with_your_account()
    {
        var person = factory.CreateTestClient();
        var email = NewEmail();
        var password = await person.RegisterConfirmedAsync(email);
        await person.SignInAsync(email, password);
        Assert.Equal(HttpStatusCode.Accepted, (await person.PostAsync("/api/v1/demo-requests", Request(email))).StatusCode);

        var export = await JsonAsync(await person.GetAsync("/api/v1/account/export"));
        var requests = export.GetProperty("sections").GetProperty("support").GetProperty("demoRequests");
        Assert.Equal("Nile Gas Stations", Assert.Single(requests.EnumerateArray()).GetProperty("company").GetString());

        Assert.True((await person.PostAsync("/api/v1/account/delete", new { password })).IsSuccessStatusCode);
        Assert.Empty(await ListedAsync(await factory.StaffAsync("Support"), email));
    }
}

public sealed class StrictFormRateLimitFactory : ApiFactory
{
    protected override IDictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["RateLimiting:FormPermitPerMinute"] = "2";
            return settings;
        }
    }
}

public class DemoRequestRateLimitTests(StrictFormRateLimitFactory factory) : IClassFixture<StrictFormRateLimitFactory>
{
    [Fact]
    public async Task The_public_form_has_a_small_budget_per_address()
    {
        var visitor = factory.CreateTestClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await visitor.PostAsync("/api/v1/demo-requests",
                new { name = "Mona", company = "Acme", email = $"lead-{i}@example.test", language = "en" })).StatusCode);

        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests], statuses);
    }
}
