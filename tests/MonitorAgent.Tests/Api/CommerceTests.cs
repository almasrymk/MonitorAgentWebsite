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

    private static async Task<Guid> PlaceAsync(Buyer buyer) =>
        (await JsonAsync(await buyer.Client.PostAsync("/api/v1/orders", new { planId = PlanId }))).GetProperty("id").GetGuid();

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
}
