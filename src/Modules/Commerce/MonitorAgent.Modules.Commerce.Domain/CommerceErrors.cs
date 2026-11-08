using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Commerce.Domain;

/// <summary>Error codes the Commerce endpoints return. They are part of the API contract.</summary>
public static class CommerceErrors
{
    public static readonly Error NoCompany = Error.NotFound("NO_COMPANY", "Create or join a company account before ordering.");
    public static readonly Error NotAllowed = Error.Forbidden("NOT_ALLOWED_FOR_ROLE", "Only company owners and billing members can order and pay.");
    public static readonly Error OrderNotFound = Error.NotFound("ORDER_NOT_FOUND", "No such order in your company.");
    public static readonly Error NotPayable = Error.Conflict("ORDER_NOT_PAYABLE", "This order is already paid or cancelled.");
    /// <summary>INT-012: one bank transfer (or receipt) can pay one order only.</summary>
    public static readonly Error PaymentReferenceUsed = Error.Conflict("PAYMENT_REFERENCE_USED", "This payment reference was already recorded for another order.");
    public static readonly Error NotRefundable = Error.Conflict("ORDER_NOT_REFUNDABLE", "Only a paid order can be refunded.");
    public static readonly Error RefundTooLarge = Error.Validation("REFUND_TOO_LARGE", "A refund must be more than zero and at most what is left of the amount paid.");
    public static readonly Error RefundReferenceUsed = Error.Conflict("REFUND_REFERENCE_USED", "This refund reference was already recorded for another order or amount.");
    /// <summary>INT-014: an unpaid upgrade order for this subscription exists; pay or cancel it first, so nobody pays twice.</summary>
    public static readonly Error UpgradeAlreadyOpen = Error.Conflict("UPGRADE_ALREADY_OPEN", "An upgrade order for this subscription is waiting for payment. Pay or cancel it first.");
    public static readonly Error NotCancellable = Error.Conflict("ORDER_NOT_CANCELLABLE", "Only unpaid orders can be cancelled.");
    /// <summary>E5: the order already waits for staff to check another transfer reference.</summary>
    public static readonly Error TransferAlreadySubmitted = Error.Conflict("PAYMENT_ALREADY_SUBMITTED", "A bank transfer for this order is already waiting to be checked.");
    /// <summary>E5: approve or reject was asked for an order with no bank transfer waiting.</summary>
    public static readonly Error NothingToVerify = Error.Conflict("NO_PAYMENT_TO_VERIFY", "This order has no bank transfer waiting to be checked.");
    /// <summary>E5: bank details are not configured, so the website does not offer bank transfer.</summary>
    public static readonly Error BankTransferUnavailable = Error.Unavailable("BANK_TRANSFER_UNAVAILABLE", "Bank transfer isn't available right now. Choose another way to pay or contact us.");
    public static readonly Error InvalidSignature = Error.Unauthorized("INVALID_SIGNATURE", "The payment notification could not be verified.");
    public static readonly Error UnknownProvider = Error.NotFound("UNKNOWN_PAYMENT_PROVIDER", "No such payment provider.");
    public static readonly Error PaymentUnavailable = Error.Unavailable("PAYMENT_UNAVAILABLE", "The payment service can't be reached right now. Try again shortly.");
}
