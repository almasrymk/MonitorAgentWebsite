using System.Net;
using System.Text.Json;

namespace MonitorAgent.Tests.Api;

/// <summary>
/// E5: the buyer pays by bank transfer and reports it; staff check the statement and approve. The license is issued (or
/// renewed) only after approval, and a reference pays one order only.
/// </summary>
public class BankTransferTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Guid PlanId = FakeLicensingPlatform.Standard.Id;

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

    private static async Task<Guid> PlaceAsync(Buyer buyer) =>
        (await JsonAsync(await buyer.Client.PostAsync("/api/v1/orders", new { planId = PlanId }))).GetProperty("id").GetGuid();

    private static Task<HttpResponseMessage> SubmitAsync(Buyer buyer, Guid orderId, string reference, string? note = null) =>
        buyer.Client.PostAsync($"/api/v1/orders/{orderId}/bank-transfer", new { reference, note });

    private async Task<HttpResponseMessage> ApproveAsync(Guid orderId) =>
        await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{orderId}/bank-transfer/approve", new { });

    private async Task<HttpResponseMessage> RejectAsync(Guid orderId, string reason = "Not on the statement") =>
        await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{orderId}/bank-transfer/reject", new { reason });

    private static async Task<JsonElement> OrderAsync(Buyer buyer, Guid orderId) => await JsonAsync(await buyer.Client.GetAsync($"/api/v1/orders/{orderId}"));

    private static async Task<int> LicensesOfAsync(Buyer buyer) => (await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses"))).GetArrayLength();

    private static async Task<int> InvoicesOfAsync(Buyer buyer, Guid orderId) =>
        (await JsonAsync(await buyer.Client.GetAsync("/api/v1/invoices"))).EnumerateArray().Count(i => i.GetProperty("orderId").GetGuid() == orderId);

    private static string NewReference() => $"TRF-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task The_buyer_sees_the_configured_account_and_the_order_number_as_the_reference()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);

        var instructions = await JsonAsync(await buyer.Client.GetAsync($"/api/v1/orders/{orderId}/bank-transfer"));

        Assert.Equal((await OrderAsync(buyer, orderId)).GetProperty("number").GetString(), instructions.GetProperty("reference").GetString());
        Assert.Equal(ApiFactory.BankAccountHolder, instructions.GetProperty("accountHolder").GetString());
        Assert.Equal(1200m, instructions.GetProperty("amount").GetDecimal());
        Assert.Equal("EGP", instructions.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task A_reported_transfer_waits_for_staff_and_issues_nothing_until_approved()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        var reference = NewReference();

        var submitted = await JsonAsync(await SubmitAsync(buyer, orderId, reference, "Sent from Example Bank on Monday"));

        Assert.Equal("PendingVerification", submitted.GetProperty("status").GetString());
        var payment = Assert.Single(submitted.GetProperty("payments").EnumerateArray());
        Assert.Equal("bank-transfer", payment.GetProperty("provider").GetString());
        Assert.Equal("Pending", payment.GetProperty("status").GetString());
        Assert.Equal(reference, payment.GetProperty("reference").GetString());
        Assert.Equal(0, await LicensesOfAsync(buyer));
        Assert.Equal(0, await InvoicesOfAsync(buyer, orderId));
        // Staff find it in their queue.
        var queue = await JsonAsync(await (await AdminAsync()).GetAsync("/api/v1/admin/orders?status=PendingVerification&pageSize=200"));
        Assert.Contains(queue.GetProperty("items").EnumerateArray(), o => o.GetProperty("id").GetGuid() == orderId);

        var approved = await JsonAsync(await ApproveAsync(orderId));

        Assert.Equal("Fulfilled", approved.GetProperty("status").GetString());
        Assert.Equal("Succeeded", Assert.Single(approved.GetProperty("payments").EnumerateArray()).GetProperty("status").GetString());
        Assert.Equal(1, await LicensesOfAsync(buyer));
        Assert.Equal(1, await InvoicesOfAsync(buyer, orderId));
    }

    [Fact]
    public async Task Reporting_the_same_transfer_twice_is_harmless_but_a_second_reference_is_refused()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        var reference = NewReference();

        await JsonAsync(await SubmitAsync(buyer, orderId, reference));
        var again = await JsonAsync(await SubmitAsync(buyer, orderId, reference));
        var other = await SubmitAsync(buyer, orderId, NewReference());

        Assert.Single(again.GetProperty("payments").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Equal("PAYMENT_ALREADY_SUBMITTED", await TestClient.ErrorCodeAsync(other));
    }

    [Fact]
    public async Task Approving_twice_or_in_parallel_pays_and_issues_once()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        await JsonAsync(await SubmitAsync(buyer, orderId, NewReference()));

        var answers = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => ApproveAsync(orderId)));
        var later = await ApproveAsync(orderId);

        foreach (var answer in answers.Append(later))
        {
            var body = await answer.Content.ReadAsStringAsync();
            Assert.True(answer.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"{(int)answer.StatusCode}: {body}");
        }
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        Assert.Equal(1, await LicensesOfAsync(buyer));
        Assert.Equal(1, await InvoicesOfAsync(buyer, orderId));
        Assert.Equal("Fulfilled", (await OrderAsync(buyer, orderId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_rejected_transfer_issues_nothing_and_can_be_reported_again()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        var reference = NewReference();
        await JsonAsync(await SubmitAsync(buyer, orderId, reference));

        var rejected = await JsonAsync(await RejectAsync(orderId));

        Assert.Equal("PendingPayment", rejected.GetProperty("status").GetString());
        Assert.Equal("Not on the statement", Assert.Single(rejected.GetProperty("payments").EnumerateArray()).GetProperty("failureReason").GetString());
        Assert.Equal("NO_PAYMENT_TO_VERIFY", await TestClient.ErrorCodeAsync(await ApproveAsync(orderId)));
        Assert.Equal(0, await LicensesOfAsync(buyer));

        // The bank finally credits it: the same reference is reported again and approved.
        await JsonAsync(await SubmitAsync(buyer, orderId, reference));
        Assert.Equal("Fulfilled", (await JsonAsync(await ApproveAsync(orderId))).GetProperty("status").GetString());
        Assert.Equal(1, await LicensesOfAsync(buyer));
    }

    [Fact]
    public async Task One_transfer_reference_cannot_pay_two_orders()
    {
        var buyer = await BuyerAsync();
        var first = await PlaceAsync(buyer);
        var second = await PlaceAsync(buyer);
        var reference = NewReference();
        await JsonAsync(await SubmitAsync(buyer, first, reference));

        var reused = await SubmitAsync(buyer, second, reference);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal("PAYMENT_REFERENCE_USED", await TestClient.ErrorCodeAsync(reused));

        // Nor can staff record the same reference as a manual payment for the other order.
        var manual = await (await AdminAsync()).PostAsync($"/api/v1/admin/orders/{second}/manual-payment", new { reference });
        Assert.Equal("PAYMENT_REFERENCE_USED", await TestClient.ErrorCodeAsync(manual));
    }

    [Fact]
    public async Task A_renewal_paid_by_transfer_is_renewed_only_after_approval()
    {
        var buyer = await BuyerAsync();
        var purchase = await PlaceAsync(buyer);
        await JsonAsync(await SubmitAsync(buyer, purchase, NewReference()));
        await JsonAsync(await ApproveAsync(purchase));
        var license = Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses"))).EnumerateArray());
        var licenseId = license.GetProperty("id").GetGuid();
        var expiresAt = license.GetProperty("expiresAt").GetDateTimeOffset();

        var renewal = (await JsonAsync(await buyer.Client.PostAsync("/api/v1/orders/renewals", new { licenseId }))).GetProperty("id").GetGuid();
        await JsonAsync(await SubmitAsync(buyer, renewal, NewReference()));
        var waiting = Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses"))).EnumerateArray());
        Assert.Equal(expiresAt, waiting.GetProperty("expiresAt").GetDateTimeOffset());

        Assert.Equal("Fulfilled", (await JsonAsync(await ApproveAsync(renewal))).GetProperty("status").GetString());
        var renewed = Assert.Single((await JsonAsync(await buyer.Client.GetAsync("/api/v1/licenses"))).EnumerateArray());
        Assert.Equal(expiresAt.AddDays(365), renewed.GetProperty("expiresAt").GetDateTimeOffset(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Another_company_cannot_see_or_report_a_transfer_for_my_order()
    {
        var mine = await BuyerAsync();
        var orderId = await PlaceAsync(mine);
        var other = await BuyerAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/v1/orders/{orderId}/bank-transfer")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SubmitAsync(other, orderId, NewReference())).StatusCode);
        Assert.Equal("PendingPayment", (await OrderAsync(mine, orderId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_buyer_cannot_approve_or_reject_a_transfer()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        await JsonAsync(await SubmitAsync(buyer, orderId, NewReference()));

        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.Client.PostAsync($"/api/v1/admin/orders/{orderId}/bank-transfer/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await buyer.Client.PostAsync($"/api/v1/admin/orders/{orderId}/bank-transfer/reject", new { reason = "x" })).StatusCode);
        Assert.Equal("PendingVerification", (await OrderAsync(buyer, orderId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_order_waiting_for_verification_cannot_be_cancelled_or_paid_by_card()
    {
        var buyer = await BuyerAsync();
        var orderId = await PlaceAsync(buyer);
        await JsonAsync(await SubmitAsync(buyer, orderId, NewReference()));

        Assert.Equal("ORDER_NOT_CANCELLABLE", await TestClient.ErrorCodeAsync(await buyer.Client.PostAsync($"/api/v1/orders/{orderId}/cancel", new { })));
        Assert.Equal("ORDER_NOT_PAYABLE", await TestClient.ErrorCodeAsync(
            await buyer.Client.PostAsync($"/api/v1/orders/{orderId}/pay", new { provider = "sandbox" })));
    }
}

/// <summary>E5: without bank details in configuration the website does not offer bank transfer.</summary>
public class BankTransferNotConfiguredTests(NoBankDetailsFactory factory) : IClassFixture<NoBankDetailsFactory>
{
    [Fact]
    public async Task Bank_transfer_is_unavailable_without_configured_details()
    {
        var client = factory.CreateTestClient();
        var email = $"user-{Guid.NewGuid():N}@example.test";
        await client.SignInAsync(email, await client.RegisterConfirmedAsync(email));
        await client.PostAsync("/api/v1/company", new { name = "Acme" });
        var order = JsonDocument.Parse(await (await client.PostAsync("/api/v1/orders", new { planId = FakeLicensingPlatform.Standard.Id })).Content.ReadAsStringAsync());
        var orderId = order.RootElement.GetProperty("id").GetGuid();

        var instructions = await client.GetAsync($"/api/v1/orders/{orderId}/bank-transfer");
        var submitted = await client.PostAsync($"/api/v1/orders/{orderId}/bank-transfer", new { reference = "TRF-1" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, instructions.StatusCode);
        Assert.Equal("BANK_TRANSFER_UNAVAILABLE", await TestClient.ErrorCodeAsync(submitted));
    }
}

public sealed class NoBankDetailsFactory : ApiFactory
{
    protected override IDictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;
            settings.Remove("Commerce:BankTransfer:AccountHolder");
            settings.Remove("Commerce:BankTransfer:Iban");
            return settings;
        }
    }
}
