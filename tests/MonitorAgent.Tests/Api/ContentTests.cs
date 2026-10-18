using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.Modules.Identity.Infrastructure.Persistence;

namespace MonitorAgent.Tests.Api;

public class ContentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    /// <summary>A staff member with the given role, signed in with two-step verification.</summary>
    private async Task<TestClient> StaffAsync(string role)
    {
        var client = factory.CreateTestClient();
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        var password = await client.RegisterConfirmedAsync(email);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            await users.RemoveFromRoleAsync(user, IdentityRoles.Customer);
            await users.AddToRoleAsync(user, role);
        }
        await client.SignInAsync(email, password);
        await client.EnableMfaAsync();
        return client;
    }

    private static object Text(string title, string body = "## Who we are\nWe build **Monitor Agent**.") => new
    {
        titleAr = $"{title} (ar)", titleEn = title, descriptionAr = "وصف", descriptionEn = "About us", bodyAr = "نص", bodyEn = body,
    };

    private static string Slug() => $"about-{Guid.NewGuid():N}"[..20];

    private static async Task<HttpResponseMessage> PublicAsync(TestClient client, string slug) => await client.GetAsync($"/api/v1/content/pages/{slug}");

    [Fact]
    public async Task A_page_reaches_the_website_only_after_someone_else_approves_it()
    {
        var editor = await StaffAsync(IdentityRoles.ContentEditor);
        var reviewer = await StaffAsync(IdentityRoles.ContentReviewer);
        var visitor = factory.CreateTestClient();
        var slug = Slug();

        var page = await JsonAsync(await editor.PostAsync("/api/v1/admin/content/pages", new { slug, content = Text("About") }));
        var id = page.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(visitor, slug)).StatusCode);

        Assert.Equal("NOT_IN_REVIEW", await TestClient.ErrorCodeAsync(await reviewer.PostAsync($"/api/v1/admin/content/pages/{id}/approve")));
        await JsonAsync(await editor.PostAsync($"/api/v1/admin/content/pages/{id}/submit"));
        Assert.Equal("PAGE_IN_REVIEW", await TestClient.ErrorCodeAsync(await editor.PutAsync($"/api/v1/admin/content/pages/{id}/draft", Text("Changed"))));
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(visitor, slug)).StatusCode);

        var approved = await JsonAsync(await reviewer.PostAsync($"/api/v1/admin/content/pages/{id}/approve"));
        Assert.Equal(1, approved.GetProperty("publishedVersion").GetInt32());
        var published = await PublicAsync(visitor, slug);
        Assert.Equal("public, max-age=60", published.Headers.CacheControl?.ToString());
        var shown = await JsonAsync(published);
        Assert.Equal("About", shown.GetProperty("titleEn").GetString());
        Assert.Equal(1, shown.GetProperty("version").GetInt32());

        // A new draft doesn't touch what visitors see until it is approved too.
        await JsonAsync(await editor.PutAsync($"/api/v1/admin/content/pages/{id}/draft", Text("About us")));
        Assert.Equal("About", (await JsonAsync(await PublicAsync(visitor, slug))).GetProperty("titleEn").GetString());
        await JsonAsync(await editor.PostAsync($"/api/v1/admin/content/pages/{id}/submit"));
        var rejected = await JsonAsync(await reviewer.PostAsync($"/api/v1/admin/content/pages/{id}/reject", new { note = "Say what we do." }));
        var draft = rejected.GetProperty("versions")[0];
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.Equal("Say what we do.", draft.GetProperty("reviewNote").GetString());

        await JsonAsync(await editor.PutAsync($"/api/v1/admin/content/pages/{id}/draft", Text("About us", "We watch your devices.")));
        await JsonAsync(await editor.PostAsync($"/api/v1/admin/content/pages/{id}/submit"));
        var second = await JsonAsync(await reviewer.PostAsync($"/api/v1/admin/content/pages/{id}/approve"));
        Assert.Equal(["Published", "Superseded"], second.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("status").GetString()));
        Assert.Equal("We watch your devices.", (await JsonAsync(await PublicAsync(visitor, slug))).GetProperty("bodyEn").GetString());

        var restored = await JsonAsync(await editor.PostAsync($"/api/v1/admin/content/pages/{id}/versions/1/restore"));
        var third = restored.GetProperty("versions")[0];
        Assert.Equal(3, third.GetProperty("number").GetInt32());
        Assert.Equal("About", third.GetProperty("titleEn").GetString());
        Assert.Equal(2, restored.GetProperty("publishedVersion").GetInt32());
    }

    [Fact]
    public async Task Nobody_approves_their_own_edit_not_even_an_admin()
    {
        var admin = await StaffAsync(IdentityRoles.Admin);
        var page = await JsonAsync(await admin.PostAsync("/api/v1/admin/content/pages", new { slug = Slug(), content = Text("Own") }));
        var id = page.GetProperty("id").GetGuid();
        await JsonAsync(await admin.PostAsync($"/api/v1/admin/content/pages/{id}/submit"));

        Assert.Equal("OWN_VERSION", await TestClient.ErrorCodeAsync(await admin.PostAsync($"/api/v1/admin/content/pages/{id}/approve")));
        Assert.Equal("OWN_VERSION", await TestClient.ErrorCodeAsync(await admin.PostAsync($"/api/v1/admin/content/pages/{id}/reject", new { note = "no" })));

        var other = await StaffAsync(IdentityRoles.Admin);
        await JsonAsync(await other.PostAsync($"/api/v1/admin/content/pages/{id}/approve"));
    }

    [Fact]
    public async Task Each_role_does_only_its_own_part()
    {
        var editor = await StaffAsync(IdentityRoles.ContentEditor);
        var reviewer = await StaffAsync(IdentityRoles.ContentReviewer);
        var support = await StaffAsync(IdentityRoles.Support);
        var customer = factory.CreateTestClient();
        var email = $"user-{Guid.NewGuid():N}@example.test";
        await customer.SignInAsync(email, await customer.RegisterConfirmedAsync(email));

        Assert.Equal("FORBIDDEN", await TestClient.ErrorCodeAsync(await reviewer.PostAsync("/api/v1/admin/content/pages", new { slug = Slug(), content = Text("X") })));
        var id = (await JsonAsync(await editor.PostAsync("/api/v1/admin/content/pages", new { slug = Slug(), content = Text("Y") }))).GetProperty("id").GetGuid();
        await JsonAsync(await editor.PostAsync($"/api/v1/admin/content/pages/{id}/submit"));

        Assert.Equal("FORBIDDEN", await TestClient.ErrorCodeAsync(await editor.PostAsync($"/api/v1/admin/content/pages/{id}/approve")));
        Assert.Equal("FORBIDDEN", await TestClient.ErrorCodeAsync(await reviewer.PostAsync($"/api/v1/admin/content/pages/{id}/withdraw")));
        Assert.Equal("FORBIDDEN", await TestClient.ErrorCodeAsync(await support.GetAsync($"/api/v1/admin/content/pages/{id}")));
        Assert.Equal("FORBIDDEN", await TestClient.ErrorCodeAsync(await support.GetAsync("/api/v1/admin/content/pages")));
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/admin/content/pages")).StatusCode);

        // The reviewer can read what they are asked to approve.
        Assert.Equal("InReview", (await JsonAsync(await reviewer.GetAsync($"/api/v1/admin/content/pages/{id}"))).GetProperty("versions")[0]
            .GetProperty("status").GetString());
        await JsonAsync(await editor.PostAsync($"/api/v1/admin/content/pages/{id}/withdraw"));
    }

    [Fact]
    public async Task Addresses_are_checked_and_unique()
    {
        var editor = await StaffAsync(IdentityRoles.ContentEditor);
        foreach (var bad in new[] { "About", "a--b", "-a", "a/b", "../x", new string('a', 61) })
        {
            var response = await editor.PostAsync("/api/v1/admin/content/pages", new { slug = bad, content = Text("Bad") });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        var slug = Slug();
        await JsonAsync(await editor.PostAsync("/api/v1/admin/content/pages", new { slug, content = Text("First") }));
        Assert.Equal("SLUG_TAKEN", await TestClient.ErrorCodeAsync(await editor.PostAsync("/api/v1/admin/content/pages", new { slug, content = Text("Again") })));

        var unfinished = await JsonAsync(await editor.PostAsync("/api/v1/admin/content/pages",
            new { slug = Slug(), content = new { titleAr = "عن", titleEn = "About", descriptionAr = "", descriptionEn = "", bodyAr = "", bodyEn = "" } }));
        Assert.Equal("PAGE_INCOMPLETE", await TestClient.ErrorCodeAsync(await editor.PostAsync($"/api/v1/admin/content/pages/{unfinished.GetProperty("id").GetGuid()}/submit")));

        var missingTitle = await editor.PostAsync("/api/v1/admin/content/pages", new { slug = Slug(), content = new { titleEn = "Only English" } });
        Assert.Equal(HttpStatusCode.BadRequest, missingTitle.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(factory.CreateTestClient(), "Not-A-Slug")).StatusCode);
    }
}
