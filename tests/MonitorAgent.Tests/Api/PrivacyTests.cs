using System.Net;
using System.Text.Json;
using System.Web;
using MonitorAgent.Modules.Identity.Contracts;

namespace MonitorAgent.Tests.Api;

public class PrivacyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "correct horse battery";

    private sealed record Person(TestClient Client, string Email);

    private async Task<Person> SignedInAsync()
    {
        var client = factory.CreateTestClient();
        var email = $"user-{Guid.NewGuid():N}@example.test";
        await client.SignInAsync(email, await client.RegisterConfirmedAsync(email, Password));
        return new Person(client, email);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private async Task<Guid> JoinAsync(Person owner, Person member, string role)
    {
        await JsonAsync(await owner.Client.PostAsync("/api/v1/company/invitations", new { email = member.Email, role }));
        var url = factory.Emails.To(member.Email)[^1].TextBody.Split('\n').Select(l => l.Trim()).First(l => l.Contains("/invitations/accept?"));
        await JsonAsync(await member.Client.PostAsync("/api/v1/company/invitations/accept", new { token = HttpUtility.ParseQueryString(new Uri(url).Query)["token"] }));
        return (await JsonAsync(await member.Client.GetAsync("/api/v1/account/me"))).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_customer_downloads_everything_held_about_them()
    {
        var person = await SignedInAsync();
        await JsonAsync(await person.Client.PostAsync("/api/v1/company", new { name = "Acme Export" }));
        Assert.True((await person.Client.PutAsync("/api/v1/company", new { name = "Acme Exported" })).IsSuccessStatusCode);

        var response = await person.Client.GetAsync("/api/v1/account/export");
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.StartsWith("attachment; filename=\"monitor-agent-data-", response.Content.Headers.ContentDisposition?.ToString());
        var export = await JsonAsync(response);

        Assert.Equal(person.Email, export.GetProperty("account").GetProperty("email").GetString());
        var sections = export.GetProperty("sections");
        Assert.Equal("Acme Exported", sections.GetProperty("company").GetProperty("membership").GetProperty("company").GetString());
        Assert.Equal("Owner", sections.GetProperty("company").GetProperty("membership").GetProperty("role").GetString());
        Assert.Contains(sections.GetProperty("securityEvents").EnumerateArray(), e => e.GetProperty("type").GetString() == "SignedIn");
        Assert.Contains(sections.GetProperty("activity").EnumerateArray(), e => e.GetProperty("action").GetString() == "company.rename");
        Assert.False(sections.TryGetProperty("orders", out _));

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateTestClient().GetAsync("/api/v1/account/export")).StatusCode);
    }

    [Fact]
    public async Task Deleting_an_account_removes_the_person_and_frees_the_email()
    {
        var owner = await SignedInAsync();
        await JsonAsync(await owner.Client.PostAsync("/api/v1/company", new { name = "Acme Delete" }));
        var member = await SignedInAsync();
        var memberId = await JoinAsync(owner, member, "Technical");

        Assert.Equal("WRONG_PASSWORD", await TestClient.ErrorCodeAsync(await owner.Client.PostAsync("/api/v1/account/delete", new { password = "not it at all" })));
        // The last owner of a team hands over first, so the company is never left without anyone in charge.
        Assert.Equal("LAST_OWNER", await TestClient.ErrorCodeAsync(await owner.Client.PostAsync("/api/v1/account/delete", new { password = Password })));
        Assert.True((await owner.Client.PutAsync($"/api/v1/company/members/{memberId}/role", new { role = "Owner" })).IsSuccessStatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PostAsync("/api/v1/account/delete", new { password = Password })).StatusCode);
        Assert.Contains(factory.Emails.To(owner.Email), m => m.Subject.Contains("Your account was deleted"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Client.GetAsync("/api/v1/account/me")).StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await TestClient.ErrorCodeAsync(await factory.CreateTestClient().LoginAsync(owner.Email, Password)));

        var company = await JsonAsync(await member.Client.GetAsync("/api/v1/company"));
        var people = company.GetProperty("members").EnumerateArray().ToList();
        Assert.Single(people);
        Assert.Equal(member.Email, people[0].GetProperty("email").GetString());

        // The address is free again: the same person can start over with a new account.
        var again = factory.CreateTestClient();
        await again.SignInAsync(owner.Email, await again.RegisterConfirmedAsync(owner.Email, Password));
        Assert.Equal("NO_COMPANY", await TestClient.ErrorCodeAsync(await again.GetAsync("/api/v1/company")));
    }

    [Fact]
    public async Task The_only_member_can_leave_and_staff_cannot_delete_themselves()
    {
        var alone = await SignedInAsync();
        await JsonAsync(await alone.Client.PostAsync("/api/v1/company", new { name = "Solo" }));
        Assert.Equal(HttpStatusCode.NoContent, (await alone.Client.PostAsync("/api/v1/account/delete", new { password = Password })).StatusCode);

        var staff = await factory.StaffAsync(IdentityRoles.Support);
        Assert.Equal("STAFF_ACCOUNT", await TestClient.ErrorCodeAsync(await staff.PostAsync("/api/v1/account/delete", new { password = Password })));
    }
}
