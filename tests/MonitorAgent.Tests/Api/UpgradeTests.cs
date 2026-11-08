using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Commerce.Application;

namespace MonitorAgent.Tests.Api;

/// <summary>
/// INT-014 (ADR-020): a dearer plan in the middle of a term is bought as a prorated upgrade order. The plan changes only
/// after payment (a bank transfer only after staff approve it); the key, the devices and the end date stay the same.
/// </summary>
public class UpgradeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Buyer(Guid CompanyId, TestClient Client);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
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

    private async Task PayManuallyAsync(Guid orderId) =>
        await JsonAsync(await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{orderId}/manual-payment", new { reference = $"BANK-TRANSFER-{Guid.NewGuid():N}" }));

    /// <summary>A Standard subscription with exactly <paramref name="daysLeft"/> whole days left.</summary>
    private async Task<(Guid LicenseId, Guid SubscriptionId, DateTimeOffset EndsAt)> StandardAsync(Buyer buyer, int daysLeft = 73)
    {
        var orderId = (await JsonAsync(await buyer.Client.PostAsync("/api/v1/orders", new { planId = FakeLicensingPlatform.Standard.Id }))).GetProperty("id").GetGuid();
        await PayManuallyAsync(orderId);
        var license = Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses"))).EnumerateArray());
        var subscriptionId = Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses/subscription"))).EnumerateArray()).GetProperty("id").GetGuid();
        var endsAt = DateTimeOffset.UtcNow.AddDays(daysLeft).AddHours(12);
        factory.Platform.SubscriptionEnds[subscriptionId] = endsAt;
        return (license.GetProperty("id").GetGuid(), subscriptionId, endsAt);
    }

    private static Task<HttpResponseMessage> QuoteAsync(Buyer buyer, Guid subscriptionId, Guid planId) =>
        buyer.Client.GetAsync($"/api/v1/orders/upgrades/quote?subscriptionId={subscriptionId}&planId={planId}");

    private static Task<HttpResponseMessage> PlaceAsync(Buyer buyer, Guid subscriptionId, Guid planId) =>
        buyer.Client.PostAsync("/api/v1/orders/upgrades", new { subscriptionId, planId });

    private static async Task<string> PlanCodeAsync(Buyer buyer) =>
        Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses/subscription"))).EnumerateArray()).GetProperty("planCode").GetString()!;

    [Fact]
    public async Task The_quote_is_the_price_difference_for_the_whole_days_left()
    {
        var buyer = await BuyerAsync();
        var (_, subscriptionId, endsAt) = await StandardAsync(buyer);

        var quote = await JsonAsync(await QuoteAsync(buyer, subscriptionId, FakeLicensingPlatform.Plus.Id));

        // (2400 - 1200) x 73 / 365 = 240.00 EGP; the half day left over is not charged.
        Assert.Equal(240.00m, quote.GetProperty("price").GetDecimal());
        Assert.Equal("EGP", quote.GetProperty("currency").GetString());
        Assert.Equal(73, quote.GetProperty("remainingDays").GetInt32());
        Assert.Equal(365, quote.GetProperty("termDays").GetInt32());
        Assert.Equal(endsAt, quote.GetProperty("termEndsAt").GetDateTimeOffset(), TimeSpan.FromSeconds(1));
        Assert.Equal("Standard", quote.GetProperty("fromPlanName").GetString());
    }

    [Fact]
    public async Task A_paid_upgrade_changes_the_plan_and_keeps_key_devices_and_end_date()
    {
        var buyer = await BuyerAsync();
        var (licenseId, subscriptionId, _) = await StandardAsync(buyer);
        var key = factory.Platform.KeyOf(licenseId);
        var expiresBefore = factory.Platform.Licenses[licenseId].License.ExpiresAt;

        var order = await JsonAsync(await PlaceAsync(buyer, subscriptionId, FakeLicensingPlatform.Plus.Id));
        Assert.Equal("Upgrade", order.GetProperty("kind").GetString());
        Assert.Equal(240.00m, order.GetProperty("total").GetDecimal());
        Assert.Equal("Standard → Plus", order.GetProperty("planName").GetString());
        // Placing the order changes nothing: the plan moves only once it is paid.
        Assert.Equal("STD", await PlanCodeAsync(buyer));

        await PayManuallyAsync(order.GetProperty("id").GetGuid());

        var paid = await JsonAsync(await buyer.Client.GetAsync($"/api/v1/orders/{order.GetProperty("id").GetGuid()}"));
        Assert.Equal("Fulfilled", paid.GetProperty("status").GetString());
        Assert.Equal("PLUS", await PlanCodeAsync(buyer));
        Assert.Equal(key, factory.Platform.KeyOf(licenseId));
        Assert.Single(factory.Platform.Licenses[licenseId].Devices);
        Assert.Equal(expiresBefore, factory.Platform.Licenses[licenseId].License.ExpiresAt);
        var invoice = Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/invoices"))).EnumerateArray(),
            i => i.GetProperty("orderId").GetGuid() == order.GetProperty("id").GetGuid());
        Assert.Equal(240.00m, invoice.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task A_paid_upgrade_replaces_a_cheaper_plan_scheduled_for_the_renewal()
    {
        var buyer = await BuyerAsync();
        var (_, subscriptionId, _) = await StandardAsync(buyer);
        var order = await JsonAsync(await PlaceAsync(buyer, subscriptionId, FakeLicensingPlatform.Plus.Id));
        await PayManuallyAsync(order.GetProperty("id").GetGuid());

        // Now on Plus; schedule Standard for the renewal, then nothing else is open.
        Assert.Equal(HttpStatusCode.NoContent,
            (await buyer.Client.PostAsync($"/api/v1/licenses/subscription/{subscriptionId}/change-plan", new { planId = FakeLicensingPlatform.Standard.Id })).StatusCode);
        var subscription = Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses/subscription"))).EnumerateArray());
        Assert.Equal(FakeLicensingPlatform.Standard.Id, subscription.GetProperty("scheduledPlanId").GetGuid());
    }

    [Fact]
    public async Task A_bank_transfer_upgrade_waits_for_staff_approval()
    {
        var buyer = await BuyerAsync();
        var (_, subscriptionId, _) = await StandardAsync(buyer);
        var orderId = (await JsonAsync(await PlaceAsync(buyer, subscriptionId, FakeLicensingPlatform.Plus.Id))).GetProperty("id").GetGuid();

        var submitted = await buyer.Client.PostAsync($"/api/v1/orders/{orderId}/bank-transfer", new { reference = $"TRF-{Guid.NewGuid():N}"[..20], note = (string?)null });
        Assert.True(submitted.IsSuccessStatusCode, await submitted.Content.ReadAsStringAsync());
        Assert.Equal("STD", await PlanCodeAsync(buyer));

        Assert.True((await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{orderId}/bank-transfer/approve", new { })).IsSuccessStatusCode);
        Assert.Equal("PLUS", await PlanCodeAsync(buyer));
    }

    [Fact]
    public async Task Only_a_dearer_plan_is_an_upgrade_and_one_order_may_be_open()
    {
        var buyer = await BuyerAsync();
        var (_, subscriptionId, _) = await StandardAsync(buyer);

        Assert.Equal("SUBSCRIPTION_SAME_PLAN", await TestClient.ErrorCodeAsync(await QuoteAsync(buyer, subscriptionId, FakeLicensingPlatform.Standard.Id)));
        await JsonAsync(await PlaceAsync(buyer, subscriptionId, FakeLicensingPlatform.Plus.Id));
        var second = await PlaceAsync(buyer, subscriptionId, FakeLicensingPlatform.Plus.Id);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("UPGRADE_ALREADY_OPEN", await TestClient.ErrorCodeAsync(second));
    }

    [Fact]
    public async Task A_term_ending_today_is_upgraded_with_the_renewal_instead()
    {
        var buyer = await BuyerAsync();
        var (_, subscriptionId, _) = await StandardAsync(buyer, daysLeft: 0);

        var quote = await QuoteAsync(buyer, subscriptionId, FakeLicensingPlatform.Plus.Id);

        Assert.Equal(HttpStatusCode.Conflict, quote.StatusCode);
        Assert.Equal("UPGRADE_TERM_ENDING", await TestClient.ErrorCodeAsync(quote));
    }

    [Fact]
    public async Task Another_company_cannot_quote_or_upgrade_my_subscription()
    {
        var owner = await BuyerAsync();
        var (_, subscriptionId, _) = await StandardAsync(owner);
        var other = await BuyerAsync();

        Assert.Equal("SUBSCRIPTION_NOT_FOUND", await TestClient.ErrorCodeAsync(await QuoteAsync(other, subscriptionId, FakeLicensingPlatform.Plus.Id)));
        Assert.Equal("SUBSCRIPTION_NOT_FOUND", await TestClient.ErrorCodeAsync(await PlaceAsync(other, subscriptionId, FakeLicensingPlatform.Plus.Id)));
        Assert.Equal("STD", await PlanCodeAsync(owner));
    }

    [Fact]
    public async Task An_upgrade_whose_answer_was_lost_is_applied_once_by_the_retry()
    {
        var buyer = await BuyerAsync();
        var (_, subscriptionId, _) = await StandardAsync(buyer);
        var orderId = (await JsonAsync(await PlaceAsync(buyer, subscriptionId, FakeLicensingPlatform.Plus.Id))).GetProperty("id").GetGuid();
        factory.Platform.LoseAnswerOf = nameof(FakeLicensingPlatform.ChangePlanAsync);

        await PayManuallyAsync(orderId);
        Assert.Equal("Paid", (await JsonAsync(await buyer.Client.GetAsync($"/api/v1/orders/{orderId}"))).GetProperty("status").GetString());
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new FulfillPaidOrdersCommand())).IsSuccess);

        Assert.Equal("Fulfilled", (await JsonAsync(await buyer.Client.GetAsync($"/api/v1/orders/{orderId}"))).GetProperty("status").GetString());
        Assert.Equal("PLUS", await PlanCodeAsync(buyer));
        Assert.Single(factory.Platform.ChangePlanCalls, c => c.SubscriptionId == subscriptionId);
    }
}
