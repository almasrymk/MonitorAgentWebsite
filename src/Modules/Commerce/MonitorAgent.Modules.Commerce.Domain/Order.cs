using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Commerce.Domain;

public enum OrderStatus { PendingPayment, Paid, Fulfilled, Cancelled }
public enum PaymentStatus { Pending, Succeeded, Failed }

/// <summary>One try at paying an order through a provider's hosted checkout.</summary>
public sealed class Payment : Entity
{
    private Payment() { }

    internal Payment(string provider, decimal amount, string currency, DateTimeOffset at)
    {
        Provider = provider;
        Amount = amount;
        Currency = currency;
        CreatedAt = at;
    }

    public string Provider { get; private set; } = "";
    /// <summary>The provider's id for this checkout or transaction, set when the checkout is created.</summary>
    public string? ProviderReference { get; internal set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "";
    public PaymentStatus Status { get; internal set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; internal set; }
    public string? FailureReason { get; internal set; }
}

public sealed record OrderPaid(Guid OrderId, string OrderNumber, Guid CompanyId, decimal Amount, string Currency) : DomainEvent;

/// <summary>
/// A company buying one plan. The price is copied from the catalog when the order is placed, so a later price change
/// never changes what an open order costs, and a payment only counts if it matches that amount and currency exactly.
/// </summary>
public sealed class Order : AggregateRoot
{
    private readonly List<Payment> _payments = [];

    private Order() { }

    public string Number { get; private set; } = "";
    public Guid CompanyId { get; private set; }
    public Guid PlacedBy { get; private set; }
    public Guid PlanId { get; private set; }
    public string ProductName { get; private set; } = "";
    public string PlanName { get; private set; } = "";
    public decimal Total { get; private set; }
    public string Currency { get; private set; } = "";
    public OrderStatus Status { get; private set; }
    public DateTimeOffset PlacedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public string? PaidVia { get; private set; }
    public string? LicenseNumber { get; private set; }
    public IReadOnlyList<Payment> Payments => _payments;

    public static Order Place(string number, Guid companyId, Guid placedBy, Guid planId, string productName, string planName,
        decimal total, string currency, DateTimeOffset at)
    {
        if (total <= 0) throw new DomainException(Error.Validation("INVALID_PRICE", "A paid plan must have a price."));
        return new Order
        {
            Number = number, CompanyId = companyId, PlacedBy = placedBy, PlanId = planId,
            ProductName = productName, PlanName = planName, Total = decimal.Round(total, 2),
            Currency = Guard.NotEmpty(currency, "Currency", 3).ToUpperInvariant(), PlacedAt = at,
        };
    }

    public Result<Payment> StartPayment(string provider, DateTimeOffset at)
    {
        if (Status != OrderStatus.PendingPayment) return CommerceErrors.NotPayable;
        var payment = new Payment(provider, Total, Currency, at);
        _payments.Add(payment);
        return payment;
    }

    public void CheckoutCreated(Guid paymentId, string providerReference) =>
        _payments.Single(p => p.Id == paymentId).ProviderReference = providerReference;

    public Payment? PaymentFor(string provider, string providerReference) =>
        _payments.SingleOrDefault(p => p.Provider == provider && p.ProviderReference == providerReference);

    /// <summary>
    /// A provider confirmed a payment. Repeats are ignored, and an amount or currency that differs from the order is
    /// recorded as a failure, never as paid.
    /// </summary>
    public bool ConfirmPayment(Payment payment, decimal amount, string currency, DateTimeOffset at)
    {
        EnsureOwn(payment);
        if (payment.Status != PaymentStatus.Pending) return false;
        payment.CompletedAt = at;
        if (amount != Total || !string.Equals(currency, Currency, StringComparison.OrdinalIgnoreCase))
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureReason = $"Paid {amount} {currency}, expected {Total} {Currency}.";
            return false;
        }

        payment.Status = PaymentStatus.Succeeded;
        if (Status != OrderStatus.PendingPayment) return false;
        MarkPaid(payment.Provider, at);
        return true;
    }

    public void FailPayment(Payment payment, string reason, DateTimeOffset at)
    {
        EnsureOwn(payment);
        if (payment.Status != PaymentStatus.Pending) return;
        payment.Status = PaymentStatus.Failed;
        payment.FailureReason = reason.Length > 200 ? reason[..200] : reason;
        payment.CompletedAt = at;
    }

    /// <summary>Staff record a payment taken outside the website (bank transfer, cash).</summary>
    public Result RecordManualPayment(string reference, DateTimeOffset at)
    {
        if (Status != OrderStatus.PendingPayment) return CommerceErrors.NotPayable;
        var payment = new Payment("manual", Total, Currency, at)
        {
            ProviderReference = Guard.NotEmpty(reference, "Payment reference", 100),
            Status = PaymentStatus.Succeeded,
            CompletedAt = at,
        };
        _payments.Add(payment);
        MarkPaid("manual", at);
        return Result.Success();
    }

    public void Fulfilled(string licenseNumber)
    {
        if (Status != OrderStatus.Paid) return;
        LicenseNumber = licenseNumber;
        Status = OrderStatus.Fulfilled;
    }

    public Result Cancel()
    {
        if (Status != OrderStatus.PendingPayment) return CommerceErrors.NotCancellable;
        Status = OrderStatus.Cancelled;
        return Result.Success();
    }

    private void EnsureOwn(Payment payment)
    {
        if (!_payments.Contains(payment)) throw new InvalidOperationException("The payment belongs to another order.");
    }

    private void MarkPaid(string via, DateTimeOffset at)
    {
        Status = OrderStatus.Paid;
        PaidAt = at;
        PaidVia = via;
        Raise(new OrderPaid(Id, Number, CompanyId, Total, Currency));
    }
}

/// <summary>A provider notification already handled. Its unique key is what makes a repeated webhook harmless.</summary>
public sealed class ProcessedNotification
{
    private ProcessedNotification() { }

    public ProcessedNotification(string provider, string eventId, DateTimeOffset at)
    {
        Provider = provider;
        EventId = eventId;
        ReceivedAt = at;
    }

    public string Provider { get; private set; } = "";
    public string EventId { get; private set; } = "";
    public DateTimeOffset ReceivedAt { get; private set; }
}

/// <summary>An issued invoice. Numbers run per year without gaps (INV-2026-000001).</summary>
public sealed class Invoice : Entity
{
    private Invoice() { }

    public Invoice(string number, Guid orderId, Guid companyId, string companyName, string description, decimal amount, string currency, DateTimeOffset at)
    {
        Number = number;
        OrderId = orderId;
        CompanyId = companyId;
        CompanyName = companyName;
        Description = description;
        Amount = amount;
        Currency = currency;
        IssuedAt = at;
    }

    public string Number { get; private set; } = "";
    public Guid OrderId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string CompanyName { get; private set; } = "";
    public string Description { get; private set; } = "";
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "";
    public DateTimeOffset IssuedAt { get; private set; }
}

public enum LedgerEntryKind { Payment, Refund }

/// <summary>Money that actually moved. Append-only; the finance reports and the nightly reconciliation read it.</summary>
public sealed class LedgerEntry : Entity
{
    private LedgerEntry() { }

    public LedgerEntry(Guid orderId, LedgerEntryKind kind, decimal amount, string currency, string provider, string reference, DateTimeOffset at)
    {
        OrderId = orderId;
        Kind = kind;
        Amount = amount;
        Currency = currency;
        Provider = provider;
        Reference = reference;
        At = at;
    }

    public Guid OrderId { get; private set; }
    public LedgerEntryKind Kind { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "";
    public string Provider { get; private set; } = "";
    public string Reference { get; private set; } = "";
    public DateTimeOffset At { get; private set; }
}
