using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MonitorAgent.Tests.Api;

public class SupportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Customer(TestClient Client, string Email);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private async Task<TestClient> StaffAsync()
    {
        if (factory.SharedAdmin is { } admin) return admin;
        admin = factory.CreateTestClient();
        await admin.SignInAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        await admin.EnableMfaAsync();
        return factory.SharedAdmin = admin;
    }

    private async Task<Customer> CustomerAsync(string company = "Acme")
    {
        var client = factory.CreateTestClient();
        var email = $"user-{Guid.NewGuid():N}@example.test";
        await client.SignInAsync(email, await client.RegisterConfirmedAsync(email));
        await JsonAsync(await client.PostAsync("/api/v1/company", new { name = company }));
        return new Customer(client, email);
    }

    private static async Task<JsonElement> OpenAsync(Customer customer, string subject = "Agent stops after update") =>
        await JsonAsync(await customer.Client.PostAsync("/api/v1/support/tickets",
            new { subject, category = "Technical", body = "Since 2.4.0 the service stops every night." }));

    [Fact]
    public async Task A_customer_opens_a_ticket_and_support_answers_it()
    {
        var customer = await CustomerAsync("Nile Traders");
        var ticket = await OpenAsync(customer);
        var id = ticket.GetProperty("id").GetGuid();
        Assert.Equal("Open", ticket.GetProperty("status").GetString());
        Assert.StartsWith("T-", ticket.GetProperty("number").GetString(), StringComparison.Ordinal);

        var staff = await StaffAsync();
        var queue = await JsonAsync(await staff.GetAsync("/api/v1/admin/support/tickets?status=Open&pageSize=100"));
        var listed = queue.GetProperty("items").EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == id);
        Assert.Equal("Nile Traders", listed.GetProperty("companyName").GetString());

        var answered = await JsonAsync(await staff.PostAsync($"/api/v1/admin/support/tickets/{id}/messages", new { body = "Please send the service log." }));
        Assert.Equal("Answered", answered.GetProperty("status").GetString());

        var email = factory.Emails.To(customer.Email)[^1];
        Assert.Contains(ticket.GetProperty("number").GetString()!, email.Subject, StringComparison.Ordinal);
        Assert.Contains($"https://portal.test/support/{id}", email.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("service log", email.TextBody, StringComparison.Ordinal);

        var seen = await JsonAsync(await customer.Client.GetAsync($"/api/v1/support/tickets/{id}"));
        var messages = seen.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(2, messages.Count);
        Assert.True(messages[1].GetProperty("fromStaff").GetBoolean());
        Assert.EndsWith("(Support)", messages[1].GetProperty("authorName").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_company_cannot_read_or_answer_a_ticket()
    {
        var owner = await CustomerAsync();
        var id = (await OpenAsync(owner)).GetProperty("id").GetGuid();
        var stranger = await CustomerAsync("Other Co");

        Assert.Equal("TICKET_NOT_FOUND", await TestClient.ErrorCodeAsync(await stranger.Client.GetAsync($"/api/v1/support/tickets/{id}")));
        Assert.Equal("TICKET_NOT_FOUND", await TestClient.ErrorCodeAsync(
            await stranger.Client.PostAsync($"/api/v1/support/tickets/{id}/messages", new { body = "hi" })));
        Assert.Equal("TICKET_NOT_FOUND", await TestClient.ErrorCodeAsync(await stranger.Client.PostAsync($"/api/v1/support/tickets/{id}/close")));
        Assert.DoesNotContain((await JsonAsync(await stranger.Client.GetAsync("/api/v1/support/tickets"))).EnumerateArray(),
            t => t.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Customers_cannot_use_the_staff_queue()
    {
        var customer = await CustomerAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Client.GetAsync("/api/v1/admin/support/tickets")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Client.GetAsync("/api/v1/admin/support/suggestions")).StatusCode);
    }

    [Fact]
    public async Task A_closed_ticket_reopens_when_the_customer_writes_again()
    {
        var customer = await CustomerAsync();
        var id = (await OpenAsync(customer)).GetProperty("id").GetGuid();
        Assert.Equal("Closed", (await JsonAsync(await customer.Client.PostAsync($"/api/v1/support/tickets/{id}/close"))).GetProperty("status").GetString());
        Assert.Equal("TICKET_CLOSED", await TestClient.ErrorCodeAsync(await customer.Client.PostAsync($"/api/v1/support/tickets/{id}/close")));

        var reopened = await JsonAsync(await customer.Client.PostAsync($"/api/v1/support/tickets/{id}/messages", new { body = "It happened again." }));
        Assert.Equal("Open", reopened.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_ticket_needs_a_subject_and_a_message()
    {
        var customer = await CustomerAsync();
        var response = await customer.Client.PostAsync("/api/v1/support/tickets", new { subject = "", category = "Technical", body = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var tooLong = await customer.Client.PostAsync("/api/v1/support/tickets", new { subject = "x", category = "Technical", body = new string('a', 5001) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Someone_without_a_company_cannot_open_tickets()
    {
        var client = factory.CreateTestClient();
        var email = $"user-{Guid.NewGuid():N}@example.test";
        await client.SignInAsync(email, await client.RegisterConfirmedAsync(email));
        Assert.Equal("NO_COMPANY", await TestClient.ErrorCodeAsync(
            await client.PostAsync("/api/v1/support/tickets", new { subject = "Help", category = "Other", body = "Hi" })));
    }

    [Fact]
    public async Task Ticket_text_is_returned_as_written_and_never_interpreted()
    {
        var customer = await CustomerAsync();
        var body = "<script>alert(1)</script> & <b>bold</b>";
        var ticket = await JsonAsync(await customer.Client.PostAsync("/api/v1/support/tickets", new { subject = "<img src=x>", category = "Other", body }));
        Assert.Equal(body, ticket.GetProperty("messages")[0].GetProperty("body").GetString());
        Assert.Equal("<img src=x>", ticket.GetProperty("subject").GetString());
    }

    [Fact]
    public async Task Support_answers_a_suggestion_and_the_customer_sees_the_answer()
    {
        var customer = await CustomerAsync();
        var suggestion = await JsonAsync(await customer.Client.PostAsync("/api/v1/support/suggestions",
            new { title = "Weekly email report", body = "A summary of device health every Monday." }));
        var id = suggestion.GetProperty("id").GetGuid();
        Assert.Equal("New", suggestion.GetProperty("status").GetString());

        var staff = await StaffAsync();
        using var request = staff.WithCsrf(new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/support/suggestions/{id}")
        {
            Content = JsonContent.Create(new { status = "Planned", note = "Coming in 2.6." }),
        });
        await JsonAsync(await staff.Http.SendAsync(request));

        var mine = (await JsonAsync(await customer.Client.GetAsync("/api/v1/support/suggestions"))).EnumerateArray().Single();
        Assert.Equal("Planned", mine.GetProperty("status").GetString());
        Assert.Equal("Coming in 2.6.", mine.GetProperty("staffNote").GetString());
    }
}
