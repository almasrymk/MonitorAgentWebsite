using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.Modules.Customers.Infrastructure.Persistence;

namespace MonitorAgent.Tests.Api;

public class CustomersTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Person(TestClient Client, string Email);

    private async Task<Person> SignedInCustomerAsync()
    {
        var client = factory.CreateTestClient();
        var email = $"user-{Guid.NewGuid():N}@example.test";
        await client.SignInAsync(email, await client.RegisterConfirmedAsync(email));
        return new Person(client, email);
    }

    private async Task<Person> OwnerOfNewCompanyAsync(string name = "Acme")
    {
        var owner = await SignedInCustomerAsync();
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.PostAsync("/api/v1/company", new { name })).StatusCode);
        return owner;
    }

    private static async Task<JsonElement> CompanyOf(Person person)
    {
        var response = await person.Client.GetAsync("/api/v1/company");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static async Task<Guid> UserIdOf(Person person) =>
        JsonDocument.Parse(await (await person.Client.GetAsync("/api/v1/account/me")).Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

    private string InvitationToken(string email)
    {
        var body = factory.Emails.To(email)[^1].TextBody;
        var url = body.Split('\n').Select(l => l.Trim()).First(l => l.Contains("/invitations/accept?", StringComparison.Ordinal));
        return HttpUtility.ParseQueryString(new Uri(url).Query)["token"]!;
    }

    private async Task<(Person Member, Guid UserId)> AddMemberAsync(Person owner, string role = "Technical")
    {
        var member = await SignedInCustomerAsync();
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.PostAsync("/api/v1/company/invitations", new { email = member.Email, role })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.Client.PostAsync("/api/v1/company/invitations/accept", new { token = InvitationToken(member.Email) })).StatusCode);
        return (member, await UserIdOf(member));
    }

    [Fact]
    public async Task A_customer_without_a_company_creates_one_and_becomes_its_owner()
    {
        var person = await SignedInCustomerAsync();
        Assert.Equal("NO_COMPANY", await TestClient.ErrorCodeAsync(await person.Client.GetAsync("/api/v1/company")));

        await person.Client.PostAsync("/api/v1/company", new { name = "Acme Trading" });
        var company = await CompanyOf(person);

        Assert.Equal("Acme Trading", company.GetProperty("name").GetString());
        Assert.Equal("Owner", company.GetProperty("myRole").GetString());
        Assert.Equal(person.Email, company.GetProperty("members")[0].GetProperty("email").GetString());
        Assert.Equal("ALREADY_IN_COMPANY", await TestClient.ErrorCodeAsync(await person.Client.PostAsync("/api/v1/company", new { name = "Second" })));
    }

    [Fact]
    public async Task An_invited_person_joins_with_the_emailed_link_and_the_role_they_were_given()
    {
        var owner = await OwnerOfNewCompanyAsync();
        var (member, _) = await AddMemberAsync(owner, "Billing");

        var seenByMember = await CompanyOf(member);
        Assert.Equal("Billing", seenByMember.GetProperty("myRole").GetString());
        Assert.Equal(0, seenByMember.GetProperty("invitations").GetArrayLength());
        Assert.Contains(seenByMember.GetProperty("members").EnumerateArray(), m => m.GetProperty("email").GetString() == owner.Email);
        Assert.Equal(2, (await CompanyOf(owner)).GetProperty("members").GetArrayLength());
    }

    [Fact]
    public async Task An_invitation_only_works_for_the_email_it_was_sent_to_and_only_once()
    {
        var owner = await OwnerOfNewCompanyAsync();
        var invited = await SignedInCustomerAsync();
        var someoneElse = await SignedInCustomerAsync();
        await owner.Client.PostAsync("/api/v1/company/invitations", new { email = invited.Email, role = "Viewer" });
        var token = InvitationToken(invited.Email);

        Assert.Equal("INVITATION_EMAIL_MISMATCH",
            await TestClient.ErrorCodeAsync(await someoneElse.Client.PostAsync("/api/v1/company/invitations/accept", new { token })));
        Assert.Equal(HttpStatusCode.OK, (await invited.Client.PostAsync("/api/v1/company/invitations/accept", new { token })).StatusCode);
        Assert.Equal("INVALID_INVITATION",
            await TestClient.ErrorCodeAsync(await someoneElse.Client.PostAsync("/api/v1/company/invitations/accept", new { token })));
        Assert.Equal("INVALID_INVITATION",
            await TestClient.ErrorCodeAsync(await someoneElse.Client.PostAsync("/api/v1/company/invitations/accept", new { token = "made-up-token" })));
    }

    [Fact]
    public async Task A_revoked_invitation_can_no_longer_be_used()
    {
        var owner = await OwnerOfNewCompanyAsync();
        var invited = await SignedInCustomerAsync();
        await owner.Client.PostAsync("/api/v1/company/invitations", new { email = invited.Email, role = "Viewer" });
        var invitationId = (await CompanyOf(owner)).GetProperty("invitations")[0].GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.Http.SendAsync(Delete($"/api/v1/company/invitations/{invitationId}", owner.Client))).StatusCode);

        Assert.Equal("INVALID_INVITATION",
            await TestClient.ErrorCodeAsync(await invited.Client.PostAsync("/api/v1/company/invitations/accept", new { token = InvitationToken(invited.Email) })));
        Assert.Equal(0, (await CompanyOf(owner)).GetProperty("invitations").GetArrayLength());
    }

    [Fact]
    public async Task Inviting_the_same_email_twice_or_an_existing_member_is_refused()
    {
        var owner = await OwnerOfNewCompanyAsync();
        var (member, _) = await AddMemberAsync(owner);
        await owner.Client.PostAsync("/api/v1/company/invitations", new { email = "new.person@example.test", role = "Viewer" });

        Assert.Equal("ALREADY_INVITED", await TestClient.ErrorCodeAsync(
            await owner.Client.PostAsync("/api/v1/company/invitations", new { email = "New.Person@example.test", role = "Viewer" })));
        Assert.Equal("ALREADY_MEMBER", await TestClient.ErrorCodeAsync(
            await owner.Client.PostAsync("/api/v1/company/invitations", new { email = member.Email, role = "Viewer" })));
    }

    [Fact]
    public async Task Only_owners_manage_the_team()
    {
        var owner = await OwnerOfNewCompanyAsync();
        var (member, memberId) = await AddMemberAsync(owner);
        var ownerId = await UserIdOf(owner);

        Assert.Equal("NOT_COMPANY_OWNER", await TestClient.ErrorCodeAsync(
            await member.Client.PostAsync("/api/v1/company/invitations", new { email = "x@example.test", role = "Owner" })));
        Assert.Equal("NOT_COMPANY_OWNER", await TestClient.ErrorCodeAsync(
            await member.Client.Http.SendAsync(Put($"/api/v1/company/members/{memberId}/role", new { role = "Owner" }, member.Client))));
        Assert.Equal("NOT_COMPANY_OWNER", await TestClient.ErrorCodeAsync(
            await member.Client.Http.SendAsync(Delete($"/api/v1/company/members/{ownerId}", member.Client))));
        Assert.Equal("NOT_COMPANY_OWNER", await TestClient.ErrorCodeAsync(
            await member.Client.Http.SendAsync(Put("/api/v1/company", new { name = "Taken over" }, member.Client))));
    }

    [Fact]
    public async Task A_company_always_keeps_an_owner_but_members_can_leave()
    {
        var owner = await OwnerOfNewCompanyAsync();
        var (member, memberId) = await AddMemberAsync(owner);
        var ownerId = await UserIdOf(owner);

        Assert.Equal("LAST_OWNER", await TestClient.ErrorCodeAsync(await owner.Client.Http.SendAsync(Delete($"/api/v1/company/members/{ownerId}", owner.Client))));
        Assert.Equal("LAST_OWNER", await TestClient.ErrorCodeAsync(
            await owner.Client.Http.SendAsync(Put($"/api/v1/company/members/{ownerId}/role", new { role = "Viewer" }, owner.Client))));

        Assert.Equal(HttpStatusCode.NoContent, (await member.Client.Http.SendAsync(Delete($"/api/v1/company/members/{memberId}", member.Client))).StatusCode);
        Assert.Equal("NO_COMPANY", await TestClient.ErrorCodeAsync(await member.Client.GetAsync("/api/v1/company")));
    }

    [Fact]
    public async Task An_owner_can_hand_over_ownership_and_then_step_down()
    {
        var owner = await OwnerOfNewCompanyAsync();
        var (member, memberId) = await AddMemberAsync(owner);
        var ownerId = await UserIdOf(owner);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.Http.SendAsync(Put($"/api/v1/company/members/{memberId}/role", new { role = "Owner" }, owner.Client))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.Http.SendAsync(Put($"/api/v1/company/members/{ownerId}/role", new { role = "Viewer" }, owner.Client))).StatusCode);

        Assert.Equal("Owner", (await CompanyOf(member)).GetProperty("myRole").GetString());
        Assert.Equal("Viewer", (await CompanyOf(owner)).GetProperty("myRole").GetString());
    }

    [Fact]
    public async Task One_company_cannot_touch_another_companys_members_or_invitations()
    {
        var ownerA = await OwnerOfNewCompanyAsync("Company A");
        var ownerB = await OwnerOfNewCompanyAsync("Company B");
        var (_, memberOfB) = await AddMemberAsync(ownerB);
        var ownerBId = await UserIdOf(ownerB);
        await ownerB.Client.PostAsync("/api/v1/company/invitations", new { email = "pending@example.test", role = "Viewer" });
        var invitationOfB = (await CompanyOf(ownerB)).GetProperty("invitations")[0].GetProperty("id").GetGuid();

        Assert.Equal("MEMBER_NOT_FOUND", await TestClient.ErrorCodeAsync(await ownerA.Client.Http.SendAsync(Delete($"/api/v1/company/members/{memberOfB}", ownerA.Client))));
        Assert.Equal("MEMBER_NOT_FOUND", await TestClient.ErrorCodeAsync(
            await ownerA.Client.Http.SendAsync(Put($"/api/v1/company/members/{ownerBId}/role", new { role = "Viewer" }, ownerA.Client))));
        Assert.Equal("INVITATION_NOT_FOUND", await TestClient.ErrorCodeAsync(await ownerA.Client.Http.SendAsync(Delete($"/api/v1/company/invitations/{invitationOfB}", ownerA.Client))));

        var b = await CompanyOf(ownerB);
        Assert.Equal("Company B", b.GetProperty("name").GetString());
        Assert.Equal(2, b.GetProperty("members").GetArrayLength());
        Assert.Equal(1, b.GetProperty("invitations").GetArrayLength());
        var a = await CompanyOf(ownerA);
        Assert.DoesNotContain(a.GetProperty("members").EnumerateArray(), m => m.GetProperty("userId").GetGuid() == memberOfB);
    }

    [Fact]
    public async Task Invitation_tokens_are_stored_only_as_hashes()
    {
        var owner = await OwnerOfNewCompanyAsync();
        await owner.Client.PostAsync("/api/v1/company/invitations", new { email = "hashed@example.test", role = "Viewer" });
        var token = InvitationToken("hashed@example.test");

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<CustomersDb>().Accounts
            .SelectMany(a => a.Invitations).Where(i => i.Email == "hashed@example.test").Select(i => i.TokenHash).SingleAsync();

        Assert.NotEqual(token, stored);
        Assert.Equal(64, stored.Length);
    }

    [Fact]
    public async Task Company_endpoints_need_a_customer_session()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateTestClient().GetAsync("/api/v1/company")).StatusCode);

        var admin = factory.CreateTestClient();
        await admin.SignInAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/company")).StatusCode);
    }

    private static HttpRequestMessage Delete(string path, TestClient client) => client.WithCsrf(new HttpRequestMessage(HttpMethod.Delete, path));

    private static HttpRequestMessage Put(string path, object body, TestClient client) =>
        client.WithCsrf(new HttpRequestMessage(HttpMethod.Put, path) { Content = System.Net.Http.Json.JsonContent.Create(body) });
}
