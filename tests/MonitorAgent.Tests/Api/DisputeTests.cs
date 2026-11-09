using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Commerce.Application;

namespace MonitorAgent.Tests.Api;

/// <summary>
/// B3: a contested payment (bank recall, chargeback, customer claim). An open dispute holds a license not issued yet; a
/// lost one records the chargeback and refunds the order; the license is never suspended automatically: staff do it
/// themselves, with a reason that is audited.
/// </summary>
public class DisputeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Buyer(Guid CompanyId, TestClient Client);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return response.StatusCode == HttpStatusCode.NoContent ? default : JsonDocument.Parse(body).RootElement;
    }

    private async Task<TestClient> AdminAsync()
    {
        if (factory.SharedAdmin is { } admin) return admin;
        admin = factory.CreateTestClient();
        await admin.SignInAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        await admin.EnableMfaAsync();
        return factory.SharedAdmin = admin;
    }

    private async Task<Buyer> BuyerAsync()
    {
        var client = factory.CreateTestClient();
        var email = $"user-{Guid.NewGuid():N}@example.test";
        await client.SignInAsync(email, await client.RegisterConfirmedAsync(email));
        var company = await JsonAsync(await client.PostAsync("/api/v1/company", new { name = "Acme" }));
        return new Buyer(company.GetProperty("id").GetGuid(), client);
    }

    private static async Task<Guid> PlaceAsync(Buyer buyer) =>
        (await JsonAsync(await buyer.Client.PostAsync("/api/v1/orders", new { planId = FakeLicensingPlatform.Standard.Id }))).GetProperty("id").GetGuid();

    private async Task<JsonElement> PayAsync(Guid orderId) =>
        await JsonAsync(await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{orderId}/manual-payment", new { reference = $"BANK-TRANSFER-{Guid.NewGuid():N}" }));

    private async Task<HttpResponseMessage> DisputeAsync(Guid orderId, string reason = "Bank recall notice 4471") =>
        await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{orderId}/dispute", new { reason });

    private async Task<HttpResponseMessage> ResolveAsync(Guid orderId, bool won, string? reference) =>
        await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{orderId}/dispute/resolve", new { won, reference });

    private static async Task<JsonElement> LicenseAsync(Buyer buyer) =>
        Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses"))).EnumerateArray());

    [Fact]
    public async Task A_lost_dispute_records_the_chargeback_and_leaves_the_license_to_staff()
    {
        var finance = await factory.StaffAsync("Finance");
        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        async Task<decimal> NetAsync()
        {
            var egp = (await JsonAsync(await finance.GetAsync($"/api/v1/admin/reports/revenue?from={today}&to={today}"))).GetProperty("totals")
                .EnumerateArray().Where(t => t.GetProperty("currency").GetString() == "EGP").ToList();
            return egp.Count == 0 ? 0m : egp[0].GetProperty("net").GetDecimal();
        }
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        await PayAsync(orderId);
        var licenseId = (await LicenseAsync(buyer)).GetProperty("id").GetGuid();
        var netBefore = await NetAsync();

        var disputed = await JsonAsync(await DisputeAsync(orderId));
        Assert.Equal("Open", disputed.GetProperty("dispute").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await ResolveAsync(orderId, won: false, reference: null)).StatusCode);
        var lost = await JsonAsync(await ResolveAsync(orderId, won: false, reference: "CB-2026-0042"));

        Assert.Equal("Lost", lost.GetProperty("dispute").GetString());
        Assert.Equal("Refunded", lost.GetProperty("status").GetString());
        Assert.Equal(1200m, lost.GetProperty("refundedAmount").GetDecimal());
        Assert.Equal(netBefore - 1200m, await NetAsync());
        // The decision about the license is the staff's, never automatic.
        Assert.Equal("Active", (await LicenseAsync(buyer)).GetProperty("status").GetString());

        var suspend = await finance.PostAsync($"/api/v1/admin/customers/{buyer.CompanyId}/licenses/{licenseId}/suspend", new { reason = "Chargeback CB-2026-0042" });
        Assert.Equal(HttpStatusCode.NoContent, suspend.StatusCode);
        Assert.Equal("Suspended", (await LicenseAsync(buyer)).GetProperty("status").GetString());
        var trail = await JsonAsync(await (await AdminAsync()).GetAsync($"/api/v1/admin/audit?action=license.&target=license:{licenseId}"));
        var entry = Assert.Single(trail.GetProperty("items").EnumerateArray());
        Assert.Equal("license.suspended-by-staff", entry.GetProperty("action").GetString());
        Assert.Contains("Chargeback CB-2026-0042", entry.GetProperty("detail").GetString(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent,
            (await finance.PostAsync($"/api/v1/admin/customers/{buyer.CompanyId}/licenses/{licenseId}/resume", new { reason = "Paid again by transfer" })).StatusCode);
        Assert.Equal("Active", (await LicenseAsync(buyer)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_open_dispute_holds_an_unissued_license_until_it_is_won()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        factory.Platform.FailNext = nameof(FakeLicensingPlatform.IssueLicenseAsync);
        Assert.Equal("Paid", (await PayAsync(orderId)).GetProperty("status").GetString());

        await JsonAsync(await DisputeAsync(orderId));
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new FulfillPaidOrdersCommand())).IsSuccess);
        Assert.Equal(0, (await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses"))).GetArrayLength());

        var won = await JsonAsync(await ResolveAsync(orderId, won: true, reference: null));
        Assert.Equal("Won", won.GetProperty("dispute").GetString());
        Assert.Equal("Fulfilled", won.GetProperty("status").GetString());
        Assert.Equal("Active", (await LicenseAsync(buyer)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Only_a_paid_order_can_be_disputed_once()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);

        Assert.Equal("ORDER_NOT_DISPUTABLE", await TestClient.ErrorCodeAsync(await DisputeAsync(orderId)));
        await PayAsync(orderId);
        await JsonAsync(await DisputeAsync(orderId));
        Assert.Equal("DISPUTE_ALREADY_OPEN", await TestClient.ErrorCodeAsync(await DisputeAsync(orderId)));
        await JsonAsync(await ResolveAsync(orderId, won: true, reference: null));
        Assert.Equal("NO_OPEN_DISPUTE", await TestClient.ErrorCodeAsync(await ResolveAsync(orderId, won: false, reference: "CB-1")));
    }

    [Fact]
    public async Task Customers_cannot_dispute_or_suspend_and_staff_cannot_cross_companies()
    {
        var owner = await BuyerAsync();
        var orderId = await PlaceAsync(owner);
        await PayAsync(orderId);
        var licenseId = (await LicenseAsync(owner)).GetProperty("id").GetGuid();
        var other = await BuyerAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.PostAsync($"/api/v1/admin/orders/{orderId}/dispute", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.Client.PostAsync($"/api/v1/admin/customers/{owner.CompanyId}/licenses/{licenseId}/suspend", new { reason = "x" })).StatusCode);
        var finance = await factory.StaffAsync("Finance");
        var wrongCompany = await finance.PostAsync($"/api/v1/admin/customers/{other.CompanyId}/licenses/{licenseId}/suspend", new { reason = "Chargeback" });
        Assert.Equal("LICENSE_NOT_FOUND", await TestClient.ErrorCodeAsync(wrongCompany));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await finance.PostAsync($"/api/v1/admin/customers/{owner.CompanyId}/licenses/{licenseId}/suspend", new { reason = "" })).StatusCode);
        Assert.Equal("Active", (await LicenseAsync(owner)).GetProperty("status").GetString());
    }
}
