using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.Modules.Licenses.Infrastructure.Persistence;

namespace MonitorAgent.Tests.Api;

public class LicensesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Guid PlanId = FakeLicensingPlatform.Standard.Id;

    private sealed record Company(Guid Id, TestClient Owner);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    // One admin session per factory: once two-step verification is on, a fresh password-only sign-in isn't enough.
    private async Task<TestClient> AdminAsync()
    {
        if (factory.SharedAdmin is { } admin) return admin;
        admin = factory.CreateTestClient();
        await admin.SignInAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        await admin.EnableMfaAsync();
        return factory.SharedAdmin = admin;
    }

    private async Task<TestClient> CustomerAsync()
    {
        var client = factory.CreateTestClient();
        var email = $"user-{Guid.NewGuid():N}@example.test";
        await client.SignInAsync(email, await client.RegisterConfirmedAsync(email));
        return client;
    }

    private async Task<Company> CompanyAsync()
    {
        var owner = await CustomerAsync();
        var company = await JsonAsync(await owner.PostAsync("/api/v1/company", new { name = "Acme" }));
        return new Company(company.GetProperty("id").GetGuid(), owner);
    }

    private async Task<HttpResponseMessage> GrantAsync(Guid companyId, string orderReference, Guid? planId = null) =>
        await (await AdminAsync()).PostAsync("/api/v1/admin/licenses/grants", new { companyId, planId = planId ?? PlanId, orderReference });

    private async Task<Guid> LicensedCompanyLicenseAsync(Company company)
    {
        await JsonAsync(await GrantAsync(company.Id, $"order-{Guid.NewGuid():N}"));
        return (await JsonAsync(await company.Owner.GetAsync("/api/v1/licenses")))[0].GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task The_price_list_is_public()
    {
        var plans = await JsonAsync(await factory.CreateTestClient().GetAsync("/api/v1/catalog/plans"));

        var plan = Assert.Single(plans.EnumerateArray(), p => p.GetProperty("code").GetString() == "STD");
        Assert.Equal("Standard", plan.GetProperty("name").GetString());
        Assert.Equal(1200m, plan.GetProperty("price").GetDecimal());
        Assert.Equal(3, plan.GetProperty("maxDevices").GetInt32());
    }

    [Fact]
    public async Task A_granted_license_shows_up_for_the_company_with_its_devices()
    {
        var company = await CompanyAsync();
        var granted = await JsonAsync(await GrantAsync(company.Id, "order-1001"));

        var licenses = await JsonAsync(await company.Owner.GetAsync("/api/v1/licenses"));
        var license = Assert.Single(licenses.EnumerateArray());
        Assert.Equal(granted.GetProperty("licenseNumber").GetString(), license.GetProperty("licenseNumber").GetString());
        Assert.Equal("Active", license.GetProperty("status").GetString());

        var details = await JsonAsync(await company.Owner.GetAsync($"/api/v1/licenses/{license.GetProperty("id").GetGuid()}"));
        Assert.Equal("Front desk PC", details.GetProperty("devices")[0].GetProperty("deviceName").GetString());
    }

    [Fact]
    public async Task Granting_the_same_order_twice_issues_one_license()
    {
        var company = await CompanyAsync();
        var subscriptionsBefore = factory.Platform.Subscriptions.Count;
        var tenantsBefore = factory.MonitorCloud.Tenants.Count;

        var first = await JsonAsync(await GrantAsync(company.Id, "order-retry"));
        var second = await JsonAsync(await GrantAsync(company.Id, "order-retry"));

        Assert.Equal(first.GetProperty("grantId").GetGuid(), second.GetProperty("grantId").GetGuid());
        Assert.Equal(subscriptionsBefore + 1, factory.Platform.Subscriptions.Count);
        Assert.Equal(tenantsBefore + 1, factory.MonitorCloud.Tenants.Count);
        Assert.Single((await JsonAsync(await company.Owner.GetAsync("/api/v1/licenses"))).EnumerateArray());
    }

    [Fact]
    public async Task A_granted_license_registers_one_monitor_tenant_for_the_company()
    {
        var company = await CompanyAsync();
        var tenantsBefore = factory.MonitorCloud.Tenants.Keys.ToHashSet();
        var customersBefore = factory.Platform.Customers.Keys.ToHashSet();
        var namesBefore = factory.MonitorCloud.Names.Count;

        await JsonAsync(await GrantAsync(company.Id, "order-monitor-1"));
        await JsonAsync(await GrantAsync(company.Id, "order-monitor-2"));

        var customerId = Assert.Single(factory.Platform.Customers.Keys, id => !customersBefore.Contains(id));
        var tenantCustomerId = Assert.Single(factory.MonitorCloud.Tenants.Keys, id => !tenantsBefore.Contains(id));
        Assert.Equal(customerId, tenantCustomerId);
        Assert.Equal(namesBefore + 1, factory.MonitorCloud.Names.Count);
        Assert.Equal("Acme", factory.MonitorCloud.Names[^1]);
    }

    [Fact]
    public async Task A_grant_interrupted_after_the_license_registers_the_tenant_on_retry()
    {
        var company = await CompanyAsync();
        var tenantsBefore = factory.MonitorCloud.Tenants.Count;
        var licensesBefore = factory.Platform.Licenses.Count;
        var subscriptionsBefore = factory.Platform.Subscriptions.Count;
        factory.MonitorCloud.FailNext = nameof(FakeMonitorCloud.RegisterTenantAsync);

        var failed = await GrantAsync(company.Id, "order-monitor-outage");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal("MONITOR_UNAVAILABLE", await TestClient.ErrorCodeAsync(failed));
        Assert.Equal(tenantsBefore, factory.MonitorCloud.Tenants.Count);
        Assert.Equal(licensesBefore + 1, factory.Platform.Licenses.Count);

        await JsonAsync(await GrantAsync(company.Id, "order-monitor-outage"));
        Assert.Equal(tenantsBefore + 1, factory.MonitorCloud.Tenants.Count);
        Assert.Equal(licensesBefore + 1, factory.Platform.Licenses.Count);
        Assert.Equal(subscriptionsBefore + 1, factory.Platform.Subscriptions.Count);
    }

    [Fact]
    public async Task An_order_reference_cannot_be_reused_for_another_company()
    {
        var a = await CompanyAsync();
        var b = await CompanyAsync();
        await JsonAsync(await GrantAsync(a.Id, "order-shared"));

        Assert.Equal("ORDER_REFERENCE_REUSED", await TestClient.ErrorCodeAsync(await GrantAsync(b.Id, "order-shared")));
        Assert.Empty((await JsonAsync(await b.Owner.GetAsync("/api/v1/licenses"))).EnumerateArray());
    }

    [Fact]
    public async Task A_grant_interrupted_by_an_outage_resumes_without_a_second_subscription()
    {
        var company = await CompanyAsync();
        var subscriptionsBefore = factory.Platform.Subscriptions.Count;
        factory.Platform.FailNext = nameof(FakeLicensingPlatform.IssueLicenseAsync);

        var failed = await GrantAsync(company.Id, "order-outage");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal("LICENSING_UNAVAILABLE", await TestClient.ErrorCodeAsync(failed));

        await JsonAsync(await GrantAsync(company.Id, "order-outage"));
        Assert.Equal(subscriptionsBefore + 1, factory.Platform.Subscriptions.Count);
        Assert.Single((await JsonAsync(await company.Owner.GetAsync("/api/v1/licenses"))).EnumerateArray());
    }

    [Fact]
    public async Task A_lost_issue_answer_gets_the_same_license_a_new_key_instead_of_a_second_license()
    {
        // INT-016: LicensingPlatform issued the license but the answer never arrived (a timeout after it committed).
        var company = await CompanyAsync();
        var licensesBefore = factory.Platform.Licenses.Count;
        factory.Platform.LoseAnswerOf = nameof(FakeLicensingPlatform.IssueLicenseAsync);

        Assert.Equal("LICENSING_UNAVAILABLE", await TestClient.ErrorCodeAsync(await GrantAsync(company.Id, "order-lost-issue")));
        Assert.Equal(licensesBefore + 1, factory.Platform.Licenses.Count);

        var granted = await JsonAsync(await GrantAsync(company.Id, "order-lost-issue"));
        Assert.Equal(licensesBefore + 1, factory.Platform.Licenses.Count);
        var license = Assert.Single((await JsonAsync(await company.Owner.GetAsync("/api/v1/licenses"))).EnumerateArray());
        Assert.Equal(granted.GetProperty("licenseNumber").GetString(), license.GetProperty("licenseNumber").GetString());
    }

    [Fact]
    public async Task Lost_customer_and_subscription_answers_create_each_only_once()
    {
        var company = await CompanyAsync();
        var customersBefore = factory.Platform.Customers.Count;
        var subscriptionsBefore = factory.Platform.Subscriptions.Count;

        factory.Platform.LoseAnswerOf = nameof(FakeLicensingPlatform.CreateCustomerAsync);
        Assert.Equal("LICENSING_UNAVAILABLE", await TestClient.ErrorCodeAsync(await GrantAsync(company.Id, "order-lost-steps")));
        factory.Platform.LoseAnswerOf = nameof(FakeLicensingPlatform.StartSubscriptionAsync);
        Assert.Equal("LICENSING_UNAVAILABLE", await TestClient.ErrorCodeAsync(await GrantAsync(company.Id, "order-lost-steps")));
        await JsonAsync(await GrantAsync(company.Id, "order-lost-steps"));

        Assert.Equal(customersBefore + 1, factory.Platform.Customers.Count);
        Assert.Equal(subscriptionsBefore + 1, factory.Platform.Subscriptions.Count);
        Assert.Single((await JsonAsync(await company.Owner.GetAsync("/api/v1/licenses"))).EnumerateArray());
    }

    [Fact]
    public async Task Parallel_fulfilments_of_one_order_issue_one_license()
    {
        var company = await CompanyAsync();
        var issuedBefore = factory.Platform.IssueCalls;

        var answers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => GrantAsync(company.Id, "order-parallel")));

        Assert.Contains(answers, a => a.IsSuccessStatusCode);
        Assert.All(answers.Where(a => !a.IsSuccessStatusCode), a => Assert.Equal(HttpStatusCode.ServiceUnavailable, a.StatusCode));
        Assert.Equal(issuedBefore + 1, factory.Platform.IssueCalls);
        await JsonAsync(await GrantAsync(company.Id, "order-parallel"));
        Assert.Single((await JsonAsync(await company.Owner.GetAsync("/api/v1/licenses"))).EnumerateArray());
    }

    [Fact]
    public async Task Only_plans_on_sale_can_be_granted()
    {
        var company = await CompanyAsync();
        Assert.Equal("PLAN_NOT_AVAILABLE", await TestClient.ErrorCodeAsync(await GrantAsync(company.Id, "order-bad-plan", Guid.NewGuid())));
    }

    [Fact]
    public async Task Owners_can_see_the_product_key_again_and_it_is_stored_encrypted()
    {
        var company = await CompanyAsync();
        var licenseId = await LicensedCompanyLicenseAsync(company);

        var revealed = await company.Owner.PostAsync($"/api/v1/licenses/{licenseId}/product-key");
        var key = (await JsonAsync(revealed)).GetProperty("productKey").GetString();

        Assert.Equal(factory.Platform.KeyOf(licenseId), key);
        Assert.Contains("no-store", revealed.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<LicensesDb>().Grants
            .Where(g => g.PlatformLicenseId == licenseId).Select(g => g.ProtectedProductKey).SingleAsync();
        Assert.DoesNotContain(key!, stored!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Viewers_and_billing_members_cannot_see_keys_or_reset_devices()
    {
        var company = await CompanyAsync();
        var licenseId = await LicensedCompanyLicenseAsync(company);
        var viewer = await CustomerAsync();
        var viewerEmail = (await JsonAsync(await viewer.GetAsync("/api/v1/account/me"))).GetProperty("email").GetString();
        await company.Owner.PostAsync("/api/v1/company/invitations", new { email = viewerEmail, role = "Viewer" });
        var link = factory.Emails.To(viewerEmail!)[^1].TextBody.Split('\n').Select(l => l.Trim()).First(l => l.Contains("token=", StringComparison.Ordinal));
        await JsonAsync(await viewer.PostAsync("/api/v1/company/invitations/accept", new { token = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query)["token"] }));
        var deviceId = (await JsonAsync(await viewer.GetAsync($"/api/v1/licenses/{licenseId}"))).GetProperty("devices")[0].GetProperty("id").GetGuid();

        Assert.Equal("NOT_ALLOWED_FOR_ROLE", await TestClient.ErrorCodeAsync(await viewer.PostAsync($"/api/v1/licenses/{licenseId}/product-key")));
        Assert.Equal("NOT_ALLOWED_FOR_ROLE", await TestClient.ErrorCodeAsync(await viewer.PostAsync($"/api/v1/licenses/{licenseId}/devices/{deviceId}/reset")));
    }

    [Fact]
    public async Task Resetting_a_device_frees_its_seat()
    {
        var company = await CompanyAsync();
        var licenseId = await LicensedCompanyLicenseAsync(company);
        var deviceId = (await JsonAsync(await company.Owner.GetAsync($"/api/v1/licenses/{licenseId}"))).GetProperty("devices")[0].GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await company.Owner.PostAsync($"/api/v1/licenses/{licenseId}/devices/{deviceId}/reset")).StatusCode);
        Assert.Equal("DEVICE_NOT_FOUND", await TestClient.ErrorCodeAsync(await company.Owner.PostAsync($"/api/v1/licenses/{licenseId}/devices/{Guid.NewGuid()}/reset")));

        var device = (await JsonAsync(await company.Owner.GetAsync($"/api/v1/licenses/{licenseId}"))).GetProperty("devices")[0];
        Assert.Equal("Deactivated", device.GetProperty("status").GetString());
    }

    [Fact]
    public async Task One_company_cannot_read_or_act_on_another_companys_license()
    {
        var a = await CompanyAsync();
        var b = await CompanyAsync();
        var licenseOfA = await LicensedCompanyLicenseAsync(a);
        await LicensedCompanyLicenseAsync(b);
        var deviceOfA = factory.Platform.Licenses[licenseOfA].Devices[0].Id;

        Assert.Equal("LICENSE_NOT_FOUND", await TestClient.ErrorCodeAsync(await b.Owner.GetAsync($"/api/v1/licenses/{licenseOfA}")));
        Assert.Equal("LICENSE_NOT_FOUND", await TestClient.ErrorCodeAsync(await b.Owner.PostAsync($"/api/v1/licenses/{licenseOfA}/devices/{deviceOfA}/reset")));
        Assert.Equal("PRODUCT_KEY_NOT_AVAILABLE", await TestClient.ErrorCodeAsync(await b.Owner.PostAsync($"/api/v1/licenses/{licenseOfA}/product-key")));
        Assert.DoesNotContain((await JsonAsync(await b.Owner.GetAsync("/api/v1/licenses"))).EnumerateArray(), l => l.GetProperty("id").GetGuid() == licenseOfA);
        Assert.Equal("Active", factory.Platform.Licenses[licenseOfA].Devices[0].Status);
    }

    [Fact]
    public async Task Only_admins_with_two_step_verification_can_grant_licenses()
    {
        var company = await CompanyAsync();
        var response = await company.Owner.PostAsync("/api/v1/admin/licenses/grants", new { companyId = company.Id, planId = PlanId, orderReference = "self-grant" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty((await JsonAsync(await company.Owner.GetAsync("/api/v1/licenses"))).EnumerateArray());
    }

    [Fact]
    public async Task The_owner_changes_plan_issues_a_device_license_and_regenerates_its_key()
    {
        var company = await CompanyAsync();
        await JsonAsync(await GrantAsync(company.Id, $"order-manage-{Guid.NewGuid():N}"));
        var subscriptionId = (await JsonAsync(await company.Owner.GetAsync("/api/v1/licenses/subscription")))[0].GetProperty("id").GetGuid();

        // INT-014: a dearer plan needs payment; the current plan is not a change.
        Assert.Equal("PLAN_UPGRADE_REQUIRES_PAYMENT", await TestClient.ErrorCodeAsync(await company.Owner.PostAsync(
            $"/api/v1/licenses/subscription/{subscriptionId}/change-plan", new { planId = FakeLicensingPlatform.Plus.Id })));
        Assert.Equal("STD", (await JsonAsync(await company.Owner.GetAsync("/api/v1/licenses/subscription")))[0].GetProperty("planCode").GetString());
        Assert.Equal("SUBSCRIPTION_SAME_PLAN", await TestClient.ErrorCodeAsync(await company.Owner.PostAsync(
            $"/api/v1/licenses/subscription/{subscriptionId}/change-plan", new { planId = FakeLicensingPlatform.Standard.Id })));

        var issued = await JsonAsync(await company.Owner.PostAsync($"/api/v1/licenses/subscription/{subscriptionId}/licenses"));
        var licenseId = issued.GetProperty("licenseId").GetGuid();
        Assert.Equal(factory.Platform.KeyOf(licenseId), issued.GetProperty("productKey").GetString());
        Assert.Equal(2, (await JsonAsync(await company.Owner.GetAsync("/api/v1/licenses"))).GetArrayLength());

        var regenerated = await JsonAsync(await company.Owner.PostAsync($"/api/v1/licenses/{licenseId}/regenerate-key"));
        Assert.NotEqual(issued.GetProperty("productKey").GetString(), regenerated.GetProperty("productKey").GetString());
        var revealed = (await JsonAsync(await company.Owner.PostAsync($"/api/v1/licenses/{licenseId}/product-key"))).GetProperty("productKey").GetString();
        Assert.Equal(regenerated.GetProperty("productKey").GetString(), revealed);

        var other = await CompanyAsync();
        Assert.Equal("SUBSCRIPTION_NOT_FOUND", await TestClient.ErrorCodeAsync(await other.Owner.PostAsync($"/api/v1/licenses/subscription/{subscriptionId}/cancel")));
        Assert.Equal(HttpStatusCode.NoContent, (await company.Owner.PostAsync($"/api/v1/licenses/subscription/{subscriptionId}/cancel")).StatusCode);
        Assert.Equal("Cancelled", (await JsonAsync(await company.Owner.GetAsync("/api/v1/licenses/subscription")))[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_company_without_licenses_sees_an_empty_list()
    {
        var company = await CompanyAsync();
        Assert.Empty((await JsonAsync(await company.Owner.GetAsync("/api/v1/licenses"))).EnumerateArray());
    }
}
