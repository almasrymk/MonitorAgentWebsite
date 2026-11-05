using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Commerce.Domain;

/// <summary>
/// Refunded (INT-019): the whole amount went back to the buyer. A partial refund leaves the status as it was.
/// PendingVerification (E5): the buyer says they paid by bank transfer; nothing is issued until staff approve it.
/// </summary>
public enum OrderStatus { PendingPayment, Paid, Fulfilled, Cancelled, Refunded, PendingVerification }
/// <summary>Purchase starts a new subscription and license; Renewal (INT-018) extends the subscription of an issued license.</summary>
public enum OrderKind { Purchase, Renewal }
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
    /// <summary>E5: what the buyer wrote when submitting a bank transfer (sender, bank, date). Never shown to other companies.</summary>
    public string? CustomerNote { get; internal set; }
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
    public OrderKind Kind { get; private set; }
    /// <summary>For a renewal: the license grant (Licenses module) whose subscription the payment extends.</summary>
    public Guid? RenewsGrantId { get; private set; }
    /// <summary>INT-019: money given back so far. Never more than <see cref="Total"/>.</summary>
    public decimal RefundedAmount { get; private set; }
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

    /// <summary>INT-018: an order that renews an issued license at the current price of <paramref name="planId"/>.</summary>
    public static Order PlaceRenewal(string number, Guid companyId, Guid placedBy, Guid grantId, Guid planId, string productName, string planName,
        decimal total, string currency, DateTimeOffset at)
    {
        var order = Place(number, companyId, placedBy, planId, productName, planName, total, currency, at);
        order.Kind = OrderKind.Renewal;
        order.RenewsGrantId = grantId;
        return order;
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
        if (Status == OrderStatus.PendingVerification && SubmittedTransfer is { } transfer)
        {
            // E5: a card payment that was already under way completed first; the reported transfer is not needed.
            transfer.Status = PaymentStatus.Failed;
            transfer.FailureReason = "Paid online instead.";
            transfer.CompletedAt = at;
            Status = OrderStatus.PendingPayment;
        }
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

    /// <summary>E5: the provider name of a bank transfer the buyer reports and staff verify.</summary>
    public const string BankTransfer = "bank-transfer";

    /// <summary>The pending bank transfer the buyer submitted, if the order is waiting for staff.</summary>
    public Payment? SubmittedTransfer =>
        _payments.LastOrDefault(p => p.Provider == BankTransfer && p.Status == PaymentStatus.Pending);

    /// <summary>
    /// E5: the buyer reports a bank transfer with the reference printed on their bank's receipt. The order waits for
    /// staff (PendingVerification); nothing is paid or issued yet. Submitting the same reference again is a no-op, and
    /// a reference staff rejected before can be submitted again once corrected at the bank.
    /// </summary>
    public Result SubmitBankTransfer(string reference, string? note, DateTimeOffset at)
    {
        reference = Guard.NotEmpty(reference, "Payment reference", 100);
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note is { Length: > 500 }) throw new DomainException(Error.Validation("NOTE_TOO_LONG", "The note can be at most 500 characters."));
        if (Status == OrderStatus.PendingVerification)
            return SubmittedTransfer?.ProviderReference == reference ? Result.Success() : CommerceErrors.TransferAlreadySubmitted;
        if (Status != OrderStatus.PendingPayment) return CommerceErrors.NotPayable;

        var payment = _payments.SingleOrDefault(p => p.Provider == BankTransfer && p.ProviderReference == reference);
        if (payment is null)
        {
            payment = new Payment(BankTransfer, Total, Currency, at) { ProviderReference = reference };
            _payments.Add(payment);
        }
        else
        {
            // Rejected before: the same transfer is looked at again.
            payment.Status = PaymentStatus.Pending;
            payment.FailureReason = null;
            payment.CompletedAt = null;
        }
        payment.CustomerNote = note;
        Status = OrderStatus.PendingVerification;
        return Result.Success();
    }

    /// <summary>
    /// E5: staff saw the money on the bank statement. Only now is the order paid (and then fulfilled by the caller).
    /// Returns false when this transfer was already approved, so a double click never pays twice.
    /// </summary>
    public Result<bool> ApproveBankTransfer(DateTimeOffset at)
    {
        if (Status is OrderStatus.Paid or OrderStatus.Fulfilled && PaidVia == BankTransfer) return false;
        if (Status != OrderStatus.PendingVerification || SubmittedTransfer is not { } payment) return CommerceErrors.NothingToVerify;
        payment.Status = PaymentStatus.Succeeded;
        payment.CompletedAt = at;
        MarkPaid(BankTransfer, at);
        return true;
    }

    /// <summary>E5: staff could not find the transfer. The order can be paid again, by any method.</summary>
    public Result RejectBankTransfer(string reason, DateTimeOffset at)
    {
        if (Status != OrderStatus.PendingVerification || SubmittedTransfer is not { } payment) return CommerceErrors.NothingToVerify;
        reason = Guard.NotEmpty(reason, "Reason", 200);
        payment.Status = PaymentStatus.Failed;
        payment.FailureReason = reason;
        payment.CompletedAt = at;
        Status = OrderStatus.PendingPayment;
        return Result.Success();
    }

    public void Fulfilled(string licenseNumber)
    {
        if (Status != OrderStatus.Paid) return;
        LicenseNumber = licenseNumber;
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>
    /// INT-019: staff record money given back outside the website (bank transfer, cash). The license is not touched:
    /// staff decide separately whether to suspend it. A refund of the whole amount before fulfilment also stops the
    /// license from being issued, because only Paid orders are fulfilled.
    /// </summary>
    public Result Refund(decimal amount)
    {
        if (Status is not (OrderStatus.Paid or OrderStatus.Fulfilled)) return CommerceErrors.NotRefundable;
        amount = decimal.Round(amount, 2);
        if (amount <= 0 || amount > Total - RefundedAmount) return CommerceErrors.RefundTooLarge;
        RefundedAmount += amount;
        if (RefundedAmount == Total) Status = OrderStatus.Refunded;
        return Result.Success();
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
