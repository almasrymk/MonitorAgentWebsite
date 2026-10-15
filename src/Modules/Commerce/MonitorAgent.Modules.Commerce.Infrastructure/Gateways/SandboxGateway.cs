using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Commerce.Application;
using MonitorAgent.Modules.Commerce.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Commerce.Infrastructure.Gateways;

public sealed class SandboxGatewayOptions
{
    /// <summary>Signs sandbox notifications. Only for development and tests; the sandbox is refused anywhere else.</summary>
    public string WebhookSecret { get; set; } = "";
}

/// <summary>
/// A stand-in provider for development and tests: the "hosted checkout" is a portal page, and notifications are
/// JSON signed with HMAC-SHA256 in <c>X-Sandbox-Signature</c>, the same way real providers sign theirs.
/// </summary>
internal sealed class SandboxGateway(SandboxGatewayOptions options, PortalLinks portal) : IPaymentGateway
{
    public const string ProviderName = "sandbox";
    public const string SignatureHeader = "X-Sandbox-Signature";

    public string Name => ProviderName;

    public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        var reference = "sbx_" + Guid.NewGuid().ToString("N");
        var url = portal.To($"checkout/sandbox?reference={reference}&amount={request.Amount.ToString(CultureInfo.InvariantCulture)}&currency={request.Currency}");
        return Task.FromResult(new CheckoutSession(reference, url));
    }

    public Result<PaymentNotification> ReadNotification(IReadOnlyDictionary<string, string> headers, string body)
    {
        if (!headers.TryGetValue(SignatureHeader, out var signature) || !Verify(body, signature)) return CommerceErrors.InvalidSignature;
        try
        {
            var n = JsonSerializer.Deserialize<Body>(body, JsonSerializerOptions.Web)!;
            return new PaymentNotification(n.EventId, n.Reference, n.Status == "succeeded", n.Amount, n.Currency, n.Reason);
        }
        catch (JsonException)
        {
            return CommerceErrors.InvalidSignature;
        }
    }

    public static string Sign(string body, string secret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    private bool Verify(string body, string signature)
    {
        var expected = Encoding.ASCII.GetBytes(Sign(body, options.WebhookSecret));
        var given = Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    private sealed record Body(string EventId, string Reference, string Status, decimal Amount, string Currency, string? Reason);
}
