using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Commerce.Domain;

/// <summary>Error codes the Commerce endpoints return. They are part of the API contract.</summary>
public static class CommerceErrors
{
    public static readonly Error NoCompany = Error.NotFound("NO_COMPANY", "Create or join a company account before ordering.");
    public static readonly Error NotAllowed = Error.Forbidden("NOT_ALLOWED_FOR_ROLE", "Only company owners and billing members can order and pay.");
    public static readonly Error OrderNotFound = Error.NotFound("ORDER_NOT_FOUND", "No such order in your company.");
    public static readonly Error NotPayable = Error.Conflict("ORDER_NOT_PAYABLE", "This order is already paid or cancelled.");
    public static readonly Error NotCancellable = Error.Conflict("ORDER_NOT_CANCELLABLE", "Only unpaid orders can be cancelled.");
    public static readonly Error InvalidSignature = Error.Unauthorized("INVALID_SIGNATURE", "The payment notification could not be verified.");
    public static readonly Error UnknownProvider = Error.NotFound("UNKNOWN_PAYMENT_PROVIDER", "No such payment provider.");
    public static readonly Error PaymentUnavailable = Error.Unavailable("PAYMENT_UNAVAILABLE", "The payment service can't be reached right now. Try again shortly.");
}
