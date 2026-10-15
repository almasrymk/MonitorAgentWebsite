using System.Net;
using System.Text.Json;
using MonitorAgent.Modules.Identity.Contracts;

namespace MonitorAgent.Tests.Api;

public class AuditTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static object Text(string title) => new { titleAr = title, titleEn = title, descriptionAr = "", descriptionEn = "", bodyAr = "نص", bodyEn = "Text" };

    [Fact]
    public async Task Sensitive_steps_are_written_with_who_did_them_and_failed_attempts_are_not()
    {
        var editor = await factory.StaffAsync(IdentityRoles.ContentEditor);
        var admin = await factory.StaffAsync(IdentityRoles.Admin);
        var slug = $"audit-{Guid.NewGuid():N}"[..20];
        var id = (await JsonAsync(await editor.PostAsync("/api/v1/admin/content/pages", new { slug, content = Text("Audit") }))).GetProperty("id").GetGuid();
        await JsonAsync(await editor.PostAsync($"/api/v1/admin/content/pages/{id}/submit"));
        // The editor isn't a reviewer: refused, so nothing is written for it.
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsync($"/api/v1/admin/content/pages/{id}/approve")).StatusCode);
        await JsonAsync(await admin.PostAsync($"/api/v1/admin/content/pages/{id}/reject", new { note = "Too short." }));

        var adminMe = await JsonAsync(await admin.GetAsync("/api/v1/account/me"));
        var editorMe = await JsonAsync(await editor.GetAsync("/api/v1/account/me"));
        var trail = await JsonAsync(await admin.GetAsync($"/api/v1/admin/audit?action=content.&target=page:{id}"));
        var entry = Assert.Single(trail.GetProperty("items").EnumerateArray());
        Assert.Equal("content.reject", entry.GetProperty("action").GetString());
        Assert.Equal("Too short.", entry.GetProperty("detail").GetString());
        Assert.Equal(adminMe.GetProperty("id").GetGuid(), entry.GetProperty("actorUserId").GetGuid());
        Assert.Equal(adminMe.GetProperty("email").GetString(), entry.GetProperty("actorEmail").GetString());

        var created = await JsonAsync(await admin.GetAsync($"/api/v1/admin/audit?actorUserId={editorMe.GetProperty("id").GetGuid()}"));
        Assert.Equal(["content.create"], created.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("action").GetString()));
        Assert.Equal($"page:{slug}", created.GetProperty("items")[0].GetProperty("target").GetString());
    }

    [Fact]
    public async Task Only_admins_read_the_audit_trail()
    {
        foreach (var role in new[] { IdentityRoles.Support, IdentityRoles.Finance, IdentityRoles.ContentReviewer })
            Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.StaffAsync(role)).GetAsync("/api/v1/admin/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateTestClient().GetAsync("/api/v1/admin/audit")).StatusCode);
    }
}
