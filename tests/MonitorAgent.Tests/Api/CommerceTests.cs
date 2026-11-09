using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Commerce.Application;
using MonitorAgent.Modules.Commerce.Infrastructure.Gateways;
using MonitorAgent.Modules.Commerce.Infrastructure.Persistence;

namespace MonitorAgent.Tests.Api;

public class CommerceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Guid PlanId = FakeLicensingPlatform.Standard.Id;

    private sealed record Buyer(Guid CompanyId, TestClient Client, string Email);

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

    private async Task<(TestClient Client, string Email)> PersonAsync()
    {
        var client = factory.CreateTestClient();
        var email = $"user-{Guid.NewGuid():N}@example.test";
        await client.SignInAsync(email, await client.RegisterConfirmedAsync(email));
        return (client, email);
    }

    private async Task<Buyer> BuyerAsync()
    {
        var (client, email) = await PersonAsync();
        var company = await JsonAsync(await client.PostAsync("/api/v1/company", new { name = "Acme" }));
        return new Buyer(company.GetProperty("id").GetGuid(), client, email);
    }

    private static async Task<Guid> PlaceAsync(Buyer buyer, Guid? planId = null) =>
        (await JsonAsync(await buyer.Client.PostAsync("/api/v1/orders", new { planId = planId ?? PlanId }))).GetProperty("id").GetGuid();

    /// <summary>Starts a sandbox checkout and returns the provider's reference for it.</summary>
    private static async Task<string> CheckoutAsync(Buyer buyer, Guid orderId)
    {
        var checkout = await JsonAsync(await buyer.Client.PostAsync($"/api/v1/orders/{orderId}/pay", new { provider = "sandbox" }));
        var url = new Uri(checkout.GetProperty("redirectUrl").GetString()!);
        return System.Web.HttpUtility.ParseQueryString(url.Query)["reference"]!;
    }

    private async Task<HttpResponseMessage> NotifyAsync(string reference, string status = "succeeded", decimal amount = 1200m, string currency = "EGP",
        string? eventId = null, string? signature = null)
    {
        var body = JsonSerializer.Serialize(new { eventId = eventId ?? $"evt_{Guid.NewGuid():N}", reference, status, amount, currency, reason = (string?)null },
            JsonSerializerOptions.Web);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/notifications/sandbox")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(SandboxGateway.SignatureHeader, signature ?? SandboxGateway.Sign(body, ApiFactory.SandboxSecret));
        // A provider has no session and no CSRF token.
        return await factory.CreateTestClient().Http.SendAsync(request);
    }

    private static async Task<JsonElement> OrderAsync(Buyer buyer, Guid orderId) => await JsonAsync(await buyer.Client.GetAsync($"/api/v1/orders/{orderId}"));

    private async Task<int> LicensesOfAsync(Buyer buyer) => (await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses"))).GetArrayLength();

    [Fact]
    public async Task A_paid_order_gets_an_invoice_and_a_license()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        var placed = await OrderAsync(buyer, orderId);
        Assert.Equal("PendingPayment", placed.GetProperty("status").GetString());
        Assert.Equal(1200m, placed.GetProperty("total").GetDecimal());

        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(await CheckoutAsync(buyer, orderId))).StatusCode);

        var order = await OrderAsync(buyer, orderId);
        Assert.Equal("Fulfilled", order.GetProperty("status").GetString());
        Assert.StartsWith("LIC-", order.GetProperty("licenseNumber").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, await LicensesOfAsync(buyer));
        var invoice = Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/invoices"))).EnumerateArray());
        Assert.Matches(@"^INV-\d{4}-\d{6}$", invoice.GetProperty("number").GetString());
        Assert.Equal(1200m, invoice.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task A_notification_delivered_twice_issues_one_license()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        var reference = await CheckoutAsync(buyer, orderId);

        // The same event resent, a second event for the same payment, and parallel deliveries.
        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(reference, eventId: "evt-dup-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(reference, eventId: "evt-dup-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(reference, eventId: "evt-dup-2")).StatusCode);
        var parallel = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => NotifyAsync(reference, eventId: "evt-dup-3")));
        Assert.All(parallel, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable }));

        Assert.Equal(1, await LicensesOfAsync(buyer));
        Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/invoices"))).EnumerateArray());
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CommerceDb>().Ledger.CountAsync(l => l.OrderId == orderId));
    }

    [Fact]
    public async Task Racing_notifications_for_a_new_payment_issue_one_license()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        var reference = await CheckoutAsync(buyer, orderId);

        // Different events for the same payment arriving together. Losers get 503 and the provider resends, as providers do.
        var pending = Enumerable.Range(0, 6).Select(i => $"evt-race-{i}").ToList();
        for (var round = 0; round < 5 && pending.Count > 0; round++)
        {
            var responses = await Task.WhenAll(pending.Select(id => NotifyAsync(reference, eventId: id)));
            Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable }));
            pending = pending.Where((_, i) => responses[i].StatusCode != HttpStatusCode.OK).ToList();
        }
        Assert.Empty(pending);

        Assert.Equal("Fulfilled", (await OrderAsync(buyer, orderId)).GetProperty("status").GetString());
        Assert.Equal(1, await LicensesOfAsync(buyer));
        Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/invoices"))).EnumerateArray());
    }

    [Fact]
    public async Task A_payment_for_the_wrong_amount_is_not_accepted()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        var reference = await CheckoutAsync(buyer, orderId);

        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(reference, amount: 1m)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(reference, currency: "USD")).StatusCode);

        Assert.NotEqual("Fulfilled", (await OrderAsync(buyer, orderId)).GetProperty("status").GetString());
        Assert.Equal(0, await LicensesOfAsync(buyer));
    }

    [Fact]
    public async Task A_notification_with_a_bad_signature_is_rejected()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        var reference = await CheckoutAsync(buyer, orderId);

        var response = await NotifyAsync(reference, signature: new string('0', 64));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("INVALID_SIGNATURE", await TestClient.ErrorCodeAsync(response));
        Assert.Equal("PendingPayment", (await OrderAsync(buyer, orderId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_declined_payment_can_be_retried()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);

        await NotifyAsync(await CheckoutAsync(buyer, orderId), status: "failed");
        Assert.Equal("PendingPayment", (await OrderAsync(buyer, orderId)).GetProperty("status").GetString());

        await NotifyAsync(await CheckoutAsync(buyer, orderId));
        var order = await OrderAsync(buyer, orderId);
        Assert.Equal("Fulfilled", order.GetProperty("status").GetString());
        Assert.Equal(2, order.GetProperty("payments").GetArrayLength());
    }

    [Fact]
    public async Task Another_company_cannot_see_pay_or_cancel_an_order()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        var other = await BuyerAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/v1/orders/{orderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync($"/api/v1/orders/{orderId}/pay", new { provider = "sandbox" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync($"/api/v1/orders/{orderId}/cancel")).StatusCode);
        Assert.Equal(0, (await JsonAsync(await other.Client.GetAsync("/api/v1/orders"))).GetArrayLength());
    }

    [Fact]
    public async Task Only_owners_and_billing_members_can_buy()
    {
        var buyer = await BuyerAsync();
        var (technical, email) = await PersonAsync();
        await JsonAsync(await buyer.Client.PostAsync("/api/v1/company/invitations", new { email, role = "Technical" }));
        var body = factory.Emails.To(email)[^1].TextBody;
        var token = body.Split('\n').Select(l => l.Trim()).First(l => l.Contains("token=", StringComparison.Ordinal)).Split("token=")[1];
        Assert.Equal(HttpStatusCode.OK, (await technical.PostAsync("/api/v1/company/invitations/accept", new { token = Uri.UnescapeDataString(token) })).StatusCode);

        var response = await technical.PostAsync("/api/v1/orders", new { planId = PlanId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("NOT_ALLOWED_FOR_ROLE", await TestClient.ErrorCodeAsync(response));
        var (loner, _) = await PersonAsync();
        Assert.Equal("NO_COMPANY", await TestClient.ErrorCodeAsync(await loner.PostAsync("/api/v1/orders", new { planId = PlanId })));
    }

    [Fact]
    public async Task A_pending_order_can_be_cancelled_and_then_not_paid()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);

        Assert.Equal(HttpStatusCode.NoContent, (await buyer.Client.PostAsync($"/api/v1/orders/{orderId}/cancel")).StatusCode);

        Assert.Equal("Cancelled", (await OrderAsync(buyer, orderId)).GetProperty("status").GetString());
        Assert.Equal("ORDER_NOT_PAYABLE",
            await TestClient.ErrorCodeAsync(await buyer.Client.PostAsync($"/api/v1/orders/{orderId}/pay", new { provider = "sandbox" })));
    }

    [Fact]
    public async Task A_paid_order_cannot_be_cancelled()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        await NotifyAsync(await CheckoutAsync(buyer, orderId));

        Assert.Equal("ORDER_NOT_CANCELLABLE", await TestClient.ErrorCodeAsync(await buyer.Client.PostAsync($"/api/v1/orders/{orderId}/cancel")));
    }

    [Fact]
    public async Task A_license_missed_while_licensing_was_down_is_issued_on_retry()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        var reference = await CheckoutAsync(buyer, orderId);

        factory.Platform.FailNext = nameof(FakeLicensingPlatform.IssueLicenseAsync);
        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(reference)).StatusCode);
        Assert.Equal("Paid", (await OrderAsync(buyer, orderId)).GetProperty("status").GetString());

        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new FulfillPaidOrdersCommand())).IsSuccess);

        Assert.Equal("Fulfilled", (await OrderAsync(buyer, orderId)).GetProperty("status").GetString());
        Assert.Equal(1, await LicensesOfAsync(buyer));
    }

    [Fact]
    public async Task Staff_can_record_a_payment_received_outside_the_website()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        var admin = await AdminAsync();

        var order = await JsonAsync(await admin.PostAsync($"/api/v1/admin/orders/{orderId}/manual-payment", new { reference = "BANK-TRANSFER-889" }));

        Assert.Equal("Fulfilled", order.GetProperty("status").GetString());
        Assert.Equal(1, await LicensesOfAsync(buyer));
        var listed = await JsonAsync(await admin.GetAsync("/api/v1/admin/orders?status=Fulfilled&pageSize=200"));
        Assert.Contains(listed.GetProperty("items").EnumerateArray(), o => o.GetProperty("id").GetGuid() == orderId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await buyer.Client.PostAsync($"/api/v1/admin/orders/{orderId}/manual-payment", new { reference = "x" })).StatusCode);
    }

    [Fact]
    public async Task Two_staff_approving_the_same_manual_payment_record_it_once()
    {
        // INT-012 (ADR-003): payments are staff-verified at launch. A double click or two finance staff at once must never
        // create a second ledger line, invoice or license, and the loser gets a clear 409, not a server error.
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        var admin = await AdminAsync();

        var answers = await Task.WhenAll(Enumerable.Range(0, 3).Select(i =>
            admin.PostAsync($"/api/v1/admin/orders/{orderId}/manual-payment", new { reference = $"BANK-TRANSFER-90{i}" })));

        Assert.Single(answers, a => a.StatusCode == HttpStatusCode.OK);
        foreach (var refused in answers.Where(a => a.StatusCode != HttpStatusCode.OK))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("ORDER_NOT_PAYABLE", await TestClient.ErrorCodeAsync(refused));
        }
        Assert.Equal(1, await LicensesOfAsync(buyer));
        Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/invoices"))).EnumerateArray(), i => i.GetProperty("orderId").GetGuid() == orderId);
        Assert.Equal("ORDER_NOT_PAYABLE", await TestClient.ErrorCodeAsync(
            await admin.PostAsync($"/api/v1/admin/orders/{orderId}/manual-payment", new { reference = "BANK-TRANSFER-999" })));
    }

    private async Task<(Guid LicenseId, DateTimeOffset ExpiresAt)> LicensedAsync(Buyer buyer, Guid? planId = null)
    {
        var orderId = await PlaceAsync(buyer, planId);
        await JsonAsync(await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{orderId}/manual-payment", new { reference = $"BANK-TRANSFER-{Guid.NewGuid():N}" }));
        var license = Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses"))).EnumerateArray());
        return (license.GetProperty("id").GetGuid(), license.GetProperty("expiresAt").GetDateTimeOffset());
    }

    private static async Task<JsonElement> SubscriptionAsync(Buyer buyer) =>
        Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses/subscription"))).EnumerateArray());

    private static Task<HttpResponseMessage> ChangePlanAsync(Buyer buyer, Guid subscriptionId, Guid planId) =>
        buyer.Client.PostAsync($"/api/v1/licenses/subscription/{subscriptionId}/change-plan", new { planId });

    [Fact]
    public async Task A_dearer_plan_is_not_applied_without_payment()
    {
        // INT-014: changing plan used to upgrade immediately and for free.
        var buyer = await BuyerAsync();
        await LicensedAsync(buyer);
        var subscriptionId = (await SubscriptionAsync(buyer)).GetProperty("id").GetGuid();

        var upgrade = await ChangePlanAsync(buyer, subscriptionId, FakeLicensingPlatform.Plus.Id);

        Assert.Equal(HttpStatusCode.Conflict, upgrade.StatusCode);
        Assert.Equal("PLAN_UPGRADE_REQUIRES_PAYMENT", await TestClient.ErrorCodeAsync(upgrade));
        var subscription = await SubscriptionAsync(buyer);
        Assert.Equal("STD", subscription.GetProperty("planCode").GetString());
        Assert.Equal(JsonValueKind.Null, subscription.GetProperty("scheduledPlanId").ValueKind);
    }

    [Fact]
    public async Task A_cheaper_plan_is_scheduled_and_applied_by_the_paid_renewal()
    {
        // INT-017: the downgrade waits for the end of the paid term; the renewal is priced at the new plan and keeps the key.
        var buyer = await BuyerAsync();
        var (licenseId, _) = await LicensedAsync(buyer, FakeLicensingPlatform.Plus.Id);
        var key = factory.Platform.KeyOf(licenseId);
        var subscriptionId = (await SubscriptionAsync(buyer)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await ChangePlanAsync(buyer, subscriptionId, FakeLicensingPlatform.Standard.Id)).StatusCode);
        var scheduled = await SubscriptionAsync(buyer);
        Assert.Equal("PLUS", scheduled.GetProperty("planCode").GetString());
        Assert.Equal(FakeLicensingPlatform.Standard.Id, scheduled.GetProperty("scheduledPlanId").GetGuid());

        var renewal = await JsonAsync(await buyer.Client.PostAsync("/api/v1/orders/renewals", new { licenseId }));
        Assert.Equal(1200m, renewal.GetProperty("total").GetDecimal());
        Assert.Equal("Standard", renewal.GetProperty("planName").GetString());
        var paid = await JsonAsync(await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{renewal.GetProperty("id").GetGuid()}/manual-payment", new { reference = $"BANK-TRANSFER-{Guid.NewGuid():N}" }));

        Assert.Equal("Fulfilled", paid.GetProperty("status").GetString());
        var after = await SubscriptionAsync(buyer);
        Assert.Equal("STD", after.GetProperty("planCode").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("scheduledPlanId").ValueKind);
        Assert.Equal(key, factory.Platform.KeyOf(licenseId));
    }

    [Fact]
    public async Task A_scheduled_change_whose_renewal_answer_was_lost_is_applied_once()
    {
        var buyer = await BuyerAsync();
        var (licenseId, expiresAt) = await LicensedAsync(buyer, FakeLicensingPlatform.Plus.Id);
        var subscriptionId = (await SubscriptionAsync(buyer)).GetProperty("id").GetGuid();
        await ChangePlanAsync(buyer, subscriptionId, FakeLicensingPlatform.Standard.Id);
        var renewal = await JsonAsync(await buyer.Client.PostAsync("/api/v1/orders/renewals", new { licenseId }));
        factory.Platform.LoseAnswerOf = nameof(FakeLicensingPlatform.RenewSubscriptionAsync);

        await JsonAsync(await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{renewal.GetProperty("id").GetGuid()}/manual-payment", new { reference = $"BANK-TRANSFER-{Guid.NewGuid():N}" }));
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new FulfillPaidOrdersCommand())).IsSuccess);

        Assert.Equal("Fulfilled", (await OrderAsync(buyer, renewal.GetProperty("id").GetGuid())).GetProperty("status").GetString());
        Assert.Equal("STD", (await SubscriptionAsync(buyer)).GetProperty("planCode").GetString());
        var license = Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses"))).EnumerateArray());
        Assert.Equal(expiresAt.AddDays(365), license.GetProperty("expiresAt").GetDateTimeOffset(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Choosing_the_current_plan_again_cancels_a_scheduled_change()
    {
        var buyer = await BuyerAsync();
        var (licenseId, _) = await LicensedAsync(buyer, FakeLicensingPlatform.Plus.Id);
        var subscriptionId = (await SubscriptionAsync(buyer)).GetProperty("id").GetGuid();
        await ChangePlanAsync(buyer, subscriptionId, FakeLicensingPlatform.Standard.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await ChangePlanAsync(buyer, subscriptionId, FakeLicensingPlatform.Plus.Id)).StatusCode);

        Assert.Equal(JsonValueKind.Null, (await SubscriptionAsync(buyer)).GetProperty("scheduledPlanId").ValueKind);
        Assert.Equal(2400m, (await JsonAsync(await buyer.Client.PostAsync("/api/v1/orders/renewals", new { licenseId }))).GetProperty("total").GetDecimal());
        Assert.Equal("SUBSCRIPTION_SAME_PLAN", await TestClient.ErrorCodeAsync(await ChangePlanAsync(buyer, subscriptionId, FakeLicensingPlatform.Plus.Id)));
    }

    [Fact]
    public async Task Another_company_cannot_schedule_a_plan_change_on_my_subscription()
    {
        var buyer = await BuyerAsync();
        await LicensedAsync(buyer, FakeLicensingPlatform.Plus.Id);
        var subscriptionId = (await SubscriptionAsync(buyer)).GetProperty("id").GetGuid();
        var other = await BuyerAsync();
        await LicensedAsync(other);

        var response = await ChangePlanAsync(other, subscriptionId, FakeLicensingPlatform.Standard.Id);

        Assert.Equal("SUBSCRIPTION_NOT_FOUND", await TestClient.ErrorCodeAsync(response));
        Assert.Equal(JsonValueKind.Null, (await SubscriptionAsync(buyer)).GetProperty("scheduledPlanId").ValueKind);
    }

    private async Task<HttpResponseMessage> RefundAsync(Guid orderId, string reference, decimal amount) =>
        await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{orderId}/refund", new { reference, amount, reason = "customer asked" });

    private async Task<int> RefundLinesAsync(Guid orderId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CommerceDb>().Ledger.CountAsync(l => l.OrderId == orderId && l.Kind == MonitorAgent.Modules.Commerce.Domain.LedgerEntryKind.Refund);
    }

    [Fact]
    public async Task A_full_refund_is_recorded_once_and_marks_the_order_refunded()
    {
        // INT-019: refunds used to have a ledger kind but no way to record one.
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        await JsonAsync(await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{orderId}/manual-payment", new { reference = $"BANK-TRANSFER-{Guid.NewGuid():N}" }));
        var reference = $"REFUND-{Guid.NewGuid():N}";

        var refunded = await JsonAsync(await RefundAsync(orderId, reference, 1200m));
        var again = await JsonAsync(await RefundAsync(orderId, reference, 1200m));

        Assert.Equal("Refunded", refunded.GetProperty("status").GetString());
        Assert.Equal(1200m, again.GetProperty("refundedAmount").GetDecimal());
        Assert.Equal(1, await RefundLinesAsync(orderId));
        // The license is left alone: staff suspend it in LicensingPlatform if they decide to.
        Assert.Equal(1, await LicensesOfAsync(buyer));
        Assert.Equal("ORDER_NOT_REFUNDABLE", await TestClient.ErrorCodeAsync(await RefundAsync(orderId, $"REFUND-{Guid.NewGuid():N}", 1m)));
    }

    [Fact]
    public async Task Partial_refunds_never_add_up_to_more_than_was_paid()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        Assert.Equal("ORDER_NOT_REFUNDABLE", await TestClient.ErrorCodeAsync(await RefundAsync(orderId, $"REFUND-{Guid.NewGuid():N}", 100m)));
        await JsonAsync(await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{orderId}/manual-payment", new { reference = $"BANK-TRANSFER-{Guid.NewGuid():N}" }));

        var part = await JsonAsync(await RefundAsync(orderId, $"REFUND-{Guid.NewGuid():N}", 200m));
        Assert.Equal("Fulfilled", part.GetProperty("status").GetString());
        Assert.Equal(200m, part.GetProperty("refundedAmount").GetDecimal());

        var tooMuch = await RefundAsync(orderId, $"REFUND-{Guid.NewGuid():N}", 1000.01m);
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        Assert.Equal("REFUND_TOO_LARGE", await TestClient.ErrorCodeAsync(tooMuch));
        Assert.Equal("Refunded", (await JsonAsync(await RefundAsync(orderId, $"REFUND-{Guid.NewGuid():N}", 1000m))).GetProperty("status").GetString());
        Assert.Equal(2, await RefundLinesAsync(orderId));
    }

    [Fact]
    public async Task A_refund_reference_cannot_be_reused_for_another_order()
    {
        var buyer = await BuyerAsync();
        var first = await PlaceAsync(buyer);
        var second = await PlaceAsync(buyer);
        foreach (var id in new[] { first, second })
            await JsonAsync(await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{id}/manual-payment", new { reference = $"BANK-TRANSFER-{Guid.NewGuid():N}" }));
        var reference = $"REFUND-{Guid.NewGuid():N}";
        await JsonAsync(await RefundAsync(first, reference, 100m));

        Assert.Equal("REFUND_REFERENCE_USED", await TestClient.ErrorCodeAsync(await RefundAsync(second, reference, 100m)));
        Assert.Equal("REFUND_REFERENCE_USED", await TestClient.ErrorCodeAsync(await RefundAsync(first, reference, 50m)));
        Assert.Equal(0, await RefundLinesAsync(second));
    }

    [Fact]
    public async Task A_paid_order_refunded_before_its_license_was_issued_is_never_issued()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        factory.Platform.FailNext = nameof(FakeLicensingPlatform.CreateCustomerAsync);
        Assert.Equal("Paid", (await JsonAsync(await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{orderId}/manual-payment",
            new { reference = $"BANK-TRANSFER-{Guid.NewGuid():N}" }))).GetProperty("status").GetString());

        await JsonAsync(await RefundAsync(orderId, $"REFUND-{Guid.NewGuid():N}", 1200m));
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new FulfillPaidOrdersCommand())).IsSuccess);

        Assert.Equal("Refunded", (await OrderAsync(buyer, orderId)).GetProperty("status").GetString());
        Assert.Equal(0, await LicensesOfAsync(buyer));
    }

    [Fact]
    public async Task A_buyer_cannot_record_a_refund()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);

        var response = await buyer.Client.PostAsync($"/api/v1/admin/orders/{orderId}/refund", new { reference = "REFUND-SELF", amount = 1200m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task One_bank_reference_cannot_pay_two_orders()
    {
        var buyer = await BuyerAsync();
        var first = await PlaceAsync(buyer);
        var second = await PlaceAsync(buyer);
        var admin = await AdminAsync();
        var reference = $"BANK-TRANSFER-{Guid.NewGuid():N}";
        await JsonAsync(await admin.PostAsync($"/api/v1/admin/orders/{first}/manual-payment", new { reference }));

        var reused = await admin.PostAsync($"/api/v1/admin/orders/{second}/manual-payment", new { reference });

        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal("PAYMENT_REFERENCE_USED", await TestClient.ErrorCodeAsync(reused));
        Assert.Equal("PendingPayment", (await OrderAsync(buyer, second)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_paid_renewal_extends_the_same_license_and_keeps_its_key()
    {
        // INT-018: renewing must not sell a second license (a new key would force every device to activate again).
        var buyer = await BuyerAsync();
        var (licenseId, expiresAt) = await LicensedAsync(buyer);
        var key = factory.Platform.KeyOf(licenseId);

        var renewal = await JsonAsync(await buyer.Client.PostAsync("/api/v1/orders/renewals", new { licenseId }));
        Assert.Equal("Renewal", renewal.GetProperty("kind").GetString());
        Assert.Equal(1200m, renewal.GetProperty("total").GetDecimal());
        var paid = await JsonAsync(await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{renewal.GetProperty("id").GetGuid()}/manual-payment", new { reference = $"BANK-TRANSFER-{Guid.NewGuid():N}" }));

        Assert.Equal("Fulfilled", paid.GetProperty("status").GetString());
        var license = Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses"))).EnumerateArray());
        Assert.Equal(licenseId, license.GetProperty("id").GetGuid());
        Assert.Equal(expiresAt.AddDays(365), license.GetProperty("expiresAt").GetDateTimeOffset(), TimeSpan.FromSeconds(1));
        Assert.Equal(key, factory.Platform.KeyOf(licenseId));
    }

    [Fact]
    public async Task A_renewal_whose_answer_was_lost_is_applied_once_by_the_retry()
    {
        var buyer = await BuyerAsync();
        var (licenseId, expiresAt) = await LicensedAsync(buyer);
        var renewal = await JsonAsync(await buyer.Client.PostAsync("/api/v1/orders/renewals", new { licenseId }));
        factory.Platform.LoseAnswerOf = nameof(FakeLicensingPlatform.RenewSubscriptionAsync);

        var paid = await JsonAsync(await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{renewal.GetProperty("id").GetGuid()}/manual-payment", new { reference = $"BANK-TRANSFER-{Guid.NewGuid():N}" }));
        Assert.Equal("Paid", paid.GetProperty("status").GetString());
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new FulfillPaidOrdersCommand())).IsSuccess);

        Assert.Equal("Fulfilled", (await OrderAsync(buyer, renewal.GetProperty("id").GetGuid())).GetProperty("status").GetString());
        var license = Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses"))).EnumerateArray());
        Assert.Equal(expiresAt.AddDays(365), license.GetProperty("expiresAt").GetDateTimeOffset(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Only_the_company_holding_a_license_can_order_its_renewal()
    {
        var owner = await BuyerAsync();
        var (licenseId, _) = await LicensedAsync(owner);
        var stranger = await BuyerAsync();

        Assert.Equal("LICENSE_NOT_FOUND", await TestClient.ErrorCodeAsync(await stranger.Client.PostAsync("/api/v1/orders/renewals", new { licenseId })));
        Assert.Equal("LICENSE_NOT_FOUND", await TestClient.ErrorCodeAsync(await owner.Client.PostAsync("/api/v1/orders/renewals", new { licenseId = Guid.NewGuid() })));
    }

    [Fact]
    public async Task A_revoked_license_cannot_be_renewed()
    {
        var buyer = await BuyerAsync();
        var (licenseId, _) = await LicensedAsync(buyer);
        await factory.Platform.RevokeLicenseAsync(licenseId, "test", default);

        Assert.Equal("LICENSE_NOT_RENEWABLE", await TestClient.ErrorCodeAsync(await buyer.Client.PostAsync("/api/v1/orders/renewals", new { licenseId })));
    }

    [Fact]
    public async Task Invoice_numbers_run_in_sequence()
    {
        var buyer = await BuyerAsync();
        for (var i = 0; i < 3; i++)
        {
            var orderId = await PlaceAsync(buyer);
            await NotifyAsync(await CheckoutAsync(buyer, orderId));
        }

        var numbers = (await JsonAsync(await buyer.Client.GetAsync("/api/v1/invoices"))).EnumerateArray()
            .Select(i => int.Parse(i.GetProperty("number").GetString()![^6..], System.Globalization.CultureInfo.InvariantCulture)).Order().ToList();
        Assert.Equal(3, numbers.Count);
        Assert.Equal(numbers[0] + 1, numbers[1]);
        Assert.Equal(numbers[1] + 1, numbers[2]);
    }

    [Fact]
    public async Task An_unknown_provider_is_refused()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);

        Assert.Equal("UNKNOWN_PAYMENT_PROVIDER",
            await TestClient.ErrorCodeAsync(await buyer.Client.PostAsync($"/api/v1/orders/{orderId}/pay", new { provider = "bitcoin" })));
    }

    [Fact]
    public async Task Checkout_lists_the_configured_providers()
    {
        var buyer = await BuyerAsync();

        var providers = await JsonAsync(await buyer.Client.GetAsync("/api/v1/payments/providers"));

        Assert.Equal("sandbox", Assert.Single(providers.EnumerateArray()).GetString());
    }

    [Fact]
    public async Task The_buyer_gets_one_receipt_and_one_license_email_even_when_the_provider_repeats_itself()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        var reference = await CheckoutAsync(buyer, orderId);
        const string eventId = "evt_receipt_once";
        await NotifyAsync(reference, eventId: eventId);
        await NotifyAsync(reference, eventId: eventId);
        await NotifyAsync(reference);

        var emails = factory.Emails.To(buyer.Email);
        var order = await OrderAsync(buyer, orderId);
        var invoice = Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/invoices"))).EnumerateArray());
        var receipt = Assert.Single(emails, m => m.Subject.Contains("Payment received", StringComparison.Ordinal));
        Assert.Contains(invoice.GetProperty("number").GetString()!, receipt.TextBody, StringComparison.Ordinal);
        Assert.Contains($"https://portal.test/invoices/{invoice.GetProperty("id").GetGuid()}", receipt.TextBody, StringComparison.Ordinal);
        var license = Assert.Single(emails, m => m.Subject.Contains("is ready", StringComparison.Ordinal));
        Assert.Contains(order.GetProperty("licenseNumber").GetString()!, license.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owners_are_reminded_once_per_window_before_a_license_expires()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        await NotifyAsync(await CheckoutAsync(buyer, orderId));
        var licenseNumber = (await OrderAsync(buyer, orderId)).GetProperty("licenseNumber").GetString()!;

        async Task ExpireInAsync(TimeSpan left)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorAgent.Modules.Licenses.Infrastructure.Persistence.LicensesDb>();
            var grant = await db.Grants.SingleAsync(g => g.LicenseNumber == licenseNumber);
            db.Entry(grant).Property(g => g.ExpiresAt).CurrentValue = DateTimeOffset.UtcNow.Add(left);
            await db.SaveChangesAsync();
        }

        async Task RemindAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new MonitorAgent.Modules.Licenses.Application.SendExpiryRemindersCommand());
        }

        int Reminders() => factory.Emails.To(buyer.Email).Count(m => m.Subject.Contains(licenseNumber, StringComparison.Ordinal) && m.Subject.Contains("expires", StringComparison.Ordinal));

        await RemindAsync();
        Assert.Equal(0, Reminders());

        await ExpireInAsync(TimeSpan.FromDays(10));
        await RemindAsync();
        await RemindAsync();
        Assert.Equal(1, Reminders());
        Assert.Contains($"https://portal.test/checkout?plan={PlanId}", factory.Emails.To(buyer.Email)[^1].TextBody, StringComparison.Ordinal);

        await ExpireInAsync(TimeSpan.FromDays(2));
        await RemindAsync();
        await RemindAsync();
        Assert.Equal(2, Reminders());
    }

    [Fact]
    public async Task Finance_sees_every_order_with_its_company_and_the_money_report()
    {
        var finance = await factory.StaffAsync("Finance");
        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var reportUrl = $"/api/v1/admin/reports/revenue?from={today}&to={today}";
        async Task<(decimal Net, int Count)> EgpAsync()
        {
            var totals = (await JsonAsync(await finance.GetAsync(reportUrl))).GetProperty("totals").EnumerateArray()
                .Where(t => t.GetProperty("currency").GetString() == "EGP").ToList();
            return totals.Count == 0 ? (0m, 0) : (totals[0].GetProperty("net").GetDecimal(), totals[0].GetProperty("paymentCount").GetInt32());
        }

        var before = await EgpAsync();
        var (client, email) = await PersonAsync();
        var company = await JsonAsync(await client.PostAsync("/api/v1/company", new { name = "=HYPERLINK(\"http://evil.test\")" }));
        var buyer = new Buyer(company.GetProperty("id").GetGuid(), client, email);
        var orderId = await PlaceAsync(buyer);
        await NotifyAsync(await CheckoutAsync(buyer, orderId));
        var number = (await OrderAsync(buyer, orderId)).GetProperty("number").GetString()!;

        var after = await EgpAsync();
        Assert.Equal(before.Net + 1200m, after.Net);
        Assert.Equal(before.Count + 1, after.Count);
        var report = await JsonAsync(await finance.GetAsync(reportUrl));
        Assert.Contains(report.GetProperty("byPlan").EnumerateArray(), p => p.GetProperty("planName").GetString() == "Standard");
        Assert.Contains(report.GetProperty("byProvider").EnumerateArray(), p => p.GetProperty("provider").GetString() == "sandbox");

        var orders = await JsonAsync(await finance.GetAsync($"/api/v1/admin/orders?companyId={buyer.CompanyId}"));
        var order = Assert.Single(orders.GetProperty("items").EnumerateArray());
        Assert.Equal(number, order.GetProperty("number").GetString());
        Assert.StartsWith("=HYPERLINK", order.GetProperty("companyName").GetString(), StringComparison.Ordinal);

        var csv = await finance.GetAsync($"/api/v1/admin/reports/ledger.csv?from={today}&to={today}");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        var text = await csv.Content.ReadAsStringAsync();
        var row = Assert.Single(text.Split("\r\n"), l => l.Contains(number, StringComparison.Ordinal));
        Assert.Contains("\"'=HYPERLINK(\"\"http://evil.test\"\")\"", row, StringComparison.Ordinal);
        Assert.Contains(",1200.00,EGP,", row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Money_reports_are_for_finance_and_admins_only()
    {
        var url = "/api/v1/admin/reports/revenue?from=2026-01-01&to=2026-12-31";
        Assert.Equal(HttpStatusCode.OK, (await (await AdminAsync()).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.StaffAsync("Support")).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.StaffAsync("ContentEditor")).GetAsync("/api/v1/admin/orders")).StatusCode);
        var finance = await factory.StaffAsync("Finance");
        Assert.Equal(HttpStatusCode.BadRequest, (await finance.GetAsync("/api/v1/admin/reports/revenue?from=2026-12-31&to=2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await finance.GetAsync("/api/v1/admin/reports/revenue?from=2020-01-01&to=2026-01-01")).StatusCode);
    }

    [Fact]
    public async Task Support_finds_a_customer_by_name_or_email_and_sees_its_people_and_licenses()
    {
        var buyer = await BuyerAsync();
        Assert.True((await buyer.Client.PutAsync("/api/v1/company", new { name = $"Delta Pharma {buyer.CompanyId:N}" })).IsSuccessStatusCode);
        var orderId = await PlaceAsync(buyer);
        await NotifyAsync(await CheckoutAsync(buyer, orderId));
        var support = await factory.StaffAsync("Support");

        var byName = await JsonAsync(await support.GetAsync($"/api/v1/admin/customers?search=Pharma%20{buyer.CompanyId:N}"));
        var found = Assert.Single(byName.GetProperty("items").EnumerateArray());
        Assert.Equal(buyer.CompanyId, found.GetProperty("id").GetGuid());
        Assert.Equal(buyer.Email, found.GetProperty("ownerEmail").GetString());

        var byEmail = await JsonAsync(await support.GetAsync($"/api/v1/admin/customers?search={Uri.EscapeDataString(buyer.Email.ToUpperInvariant())}"));
        Assert.Equal(buyer.CompanyId, Assert.Single(byEmail.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var nobody = await JsonAsync(await support.GetAsync("/api/v1/admin/customers?search=nobody%40example.test"));
        Assert.Equal(0, nobody.GetProperty("totalCount").GetInt32());

        var details = await JsonAsync(await support.GetAsync($"/api/v1/admin/customers/{buyer.CompanyId}"));
        var owner = Assert.Single(details.GetProperty("members").EnumerateArray());
        Assert.Equal("Owner", owner.GetProperty("role").GetString());
        Assert.Equal(buyer.Email, owner.GetProperty("email").GetString());
        var licenses = await JsonAsync(await support.GetAsync($"/api/v1/admin/customers/{buyer.CompanyId}/licenses"));
        Assert.Equal(1, licenses.GetArrayLength());

        Assert.Equal("COMPANY_NOT_FOUND", await TestClient.ErrorCodeAsync(await support.GetAsync($"/api/v1/admin/customers/{Guid.NewGuid()}")));
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.StaffAsync("ContentEditor")).GetAsync("/api/v1/admin/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.Client.GetAsync("/api/v1/admin/customers")).StatusCode);
    }
}
