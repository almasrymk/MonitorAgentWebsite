using MonitorAgent.Modules.Commerce.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Commerce.Application;

public sealed record CheckoutRequest(Guid OrderId, string OrderNumber, Guid PaymentId, decimal Amount, string Currency,
    string Description, string CustomerEmail, string ReturnUrl);

public sealed record CheckoutSession(string ProviderReference, string RedirectUrl);

/// <summary>What a provider told us about a payment, after its signature was verified.</summary>
public sealed record PaymentNotification(string EventId, string ProviderReference, bool Succeeded, decimal Amount, string Currency, string? FailureReason);

/// <summary>Thrown by a gateway when the provider can't be reached.</summary>
public sealed class PaymentProviderUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// One payment provider. Card data never touches this application: the customer pays on the provider's hosted page,
/// and the result arrives as a signed server-to-server notification.
/// </summary>
public interface IPaymentGateway
{
    string Name { get; }
    Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct);

    /// <summary>Verifies the notification's signature and reads it. A bad signature is INVALID_SIGNATURE.</summary>
    Result<PaymentNotification> ReadNotification(IReadOnlyDictionary<string, string> headers, string body);
}

/// <summary>Thrown by <see cref="ICommerceStore.SaveAsync"/> when another request saved the same thing first.</summary>
public sealed class SaveConflictException(Exception inner) : Exception("A concurrent update won.", inner);

internal sealed record LedgerLine(LedgerEntry Entry, string OrderNumber, string ProductName, string PlanName, Guid CompanyId);

internal interface ICommerceStore
{
    Task<Order?> OrderAsync(Guid orderId, CancellationToken ct);
    Task<Order?> OrderForPaymentAsync(string provider, string providerReference, CancellationToken ct);
    Task<IReadOnlyList<Order>> OrdersOfAsync(Guid companyId, CancellationToken ct);
    Task<(IReadOnlyList<Order> Items, int Total)> OrdersAsync(OrderStatus? status, Guid? companyId, int page, int pageSize, CancellationToken ct);
    /// <summary>Money moved between the two instants, oldest first, with the order each line belongs to. At most <paramref name="max"/> lines.</summary>
    Task<IReadOnlyList<LedgerLine>> LedgerAsync(DateTimeOffset from, DateTimeOffset until, int max, CancellationToken ct);
    Task<IReadOnlyList<Order>> PaidUnfulfilledAsync(int max, CancellationToken ct);
    Task<bool> NotificationSeenAsync(string provider, string eventId, CancellationToken ct);
    void Add(Order order);
    void Add(ProcessedNotification notification);
    void Add(LedgerEntry entry);
    Task<Invoice?> InvoiceForOrderAsync(Guid orderId, CancellationToken ct);
    Task<IReadOnlyList<Invoice>> InvoicesOfAsync(Guid companyId, CancellationToken ct);
    /// <summary>Takes the next invoice number for the year. Part of the same save as the invoice itself.</summary>
    Task<string> NextInvoiceNumberAsync(int year, CancellationToken ct);
    void Add(Invoice invoice);
    /// <exception cref="SaveConflictException">A unique key or counter was taken by a concurrent request.</exception>
    Task SaveAsync(CancellationToken ct);
}
