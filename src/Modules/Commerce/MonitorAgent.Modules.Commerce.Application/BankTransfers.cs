using FluentValidation;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Commerce.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Commerce.Application;

/// <summary>
/// E5: the company's bank account for transfers. Comes from configuration (environment variables in production,
/// Commerce__BankTransfer__Iban etc.) and is never in the repository. Without a holder and an account number or IBAN,
/// bank transfer is not offered.
/// </summary>
public sealed class BankTransferOptions
{
    public string? AccountHolder { get; set; }
    public string? BankName { get; set; }
    public string? AccountNumber { get; set; }
    public string? Iban { get; set; }
    public string? Swift { get; set; }
    /// <summary>Free text shown under the details, for example how long checking a transfer takes.</summary>
    public string? Instructions { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AccountHolder)
        && (!string.IsNullOrWhiteSpace(AccountNumber) || !string.IsNullOrWhiteSpace(Iban));
}

/// <summary>What the buyer needs to make the transfer. <paramref name="Reference"/> is the order number, unique per order.</summary>
public sealed record BankTransferInstructionsDto(string Reference, decimal Amount, string Currency, string AccountHolder, string? BankName,
    string? AccountNumber, string? Iban, string? Swift, string? Instructions);

public sealed record GetBankTransferInstructionsQuery(Guid OrderId) : IQuery<Result<BankTransferInstructionsDto>>;

/// <summary>
/// E5: the buyer reports a transfer. The order waits for staff (PendingVerification) and nothing is issued. Sending the
/// same reference again returns the order unchanged.
/// </summary>
public sealed record SubmitBankTransferCommand(Guid OrderId, string Reference, string? Note) : ICommand<Result<OrderDto>>, IAuditedCommand
{
    public string AuditAction => "order.bank-transfer.submit";
    public string AuditTarget => $"order:{OrderId}";
    public string? AuditDetail => Reference;
}

/// <summary>E5: staff found the money on the statement. The order is paid and the license issued (or renewed). Idempotent.</summary>
public sealed record ApproveBankTransferCommand(Guid OrderId) : ICommand<Result<OrderDto>>, IAuditedCommand
{
    public string AuditAction => "order.bank-transfer.approve";
    public string AuditTarget => $"order:{OrderId}";
}

/// <summary>E5: staff could not find the transfer. The order goes back to waiting for payment.</summary>
public sealed record RejectBankTransferCommand(Guid OrderId, string Reason) : ICommand<Result<OrderDto>>, IAuditedCommand
{
    public string AuditAction => "order.bank-transfer.reject";
    public string AuditTarget => $"order:{OrderId}";
    public string? AuditDetail => Reason;
}

internal sealed class SubmitBankTransferValidator : AbstractValidator<SubmitBankTransferCommand>
{
    public SubmitBankTransferValidator()
    {
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

internal sealed class RejectBankTransferValidator : AbstractValidator<RejectBankTransferCommand>
{
    public RejectBankTransferValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
}

internal static class BankTransfers
{
    /// <summary>
    /// INT-012 / E5: one bank transfer pays one order. A reference counts as used if staff recorded it as a manual payment
    /// or a buyer reported it for another order.
    /// </summary>
    public static async Task<bool> ReferenceUsedElsewhereAsync(ICommerceStore store, Order order, string reference, CancellationToken ct)
    {
        foreach (var provider in new[] { "manual", Order.BankTransfer })
        {
            if (await store.OrderForPaymentAsync(provider, reference, ct) is { } other && other.Id != order.Id) return true;
        }
        return false;
    }
}

internal sealed class GetBankTransferInstructionsHandler(BuyerScope scope, ICommerceStore store, BankTransferOptions bank)
    : IQueryHandler<GetBankTransferInstructionsQuery, Result<BankTransferInstructionsDto>>
{
    public async Task<Result<BankTransferInstructionsDto>> HandleAsync(GetBankTransferInstructionsQuery query, CancellationToken ct)
    {
        var found = await scope.OwnOrderAsync(store, query.OrderId, ct);
        if (found.IsFailure) return found.Error!;
        if (!bank.IsConfigured) return CommerceErrors.BankTransferUnavailable;
        var order = found.Value;
        if (order.Status is not (OrderStatus.PendingPayment or OrderStatus.PendingVerification)) return CommerceErrors.NotPayable;
        return new BankTransferInstructionsDto(order.Number, order.Total, order.Currency, bank.AccountHolder!.Trim(), Clean(bank.BankName),
            Clean(bank.AccountNumber), Clean(bank.Iban), Clean(bank.Swift), Clean(bank.Instructions));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class SubmitBankTransferHandler(BuyerScope scope, ICommerceStore store, BankTransferOptions bank, TimeProvider clock)
    : ICommandHandler<SubmitBankTransferCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> HandleAsync(SubmitBankTransferCommand command, CancellationToken ct)
    {
        var found = await scope.OwnOrderAsync(store, command.OrderId, ct);
        if (found.IsFailure) return found.Error!;
        if (!bank.IsConfigured) return CommerceErrors.BankTransferUnavailable;
        var order = found.Value;
        var reference = command.Reference.Trim();
        if (await BankTransfers.ReferenceUsedElsewhereAsync(store, order, reference, ct)) return CommerceErrors.PaymentReferenceUsed;

        var submitted = order.SubmitBankTransfer(reference, command.Note, clock.GetUtcNow());
        if (submitted.IsFailure) return submitted.Error!;
        try
        {
            await store.SaveAsync(ct);
        }
        catch (SaveConflictException)
        {
            // The reference was taken by another order in parallel (unique per provider), or this order changed.
            return CommerceErrors.PaymentReferenceUsed;
        }
        return order.ToDto();
    }
}

internal sealed class ApproveBankTransferHandler(ICommerceStore store, AfterPayment after, TimeProvider clock)
    : ICommandHandler<ApproveBankTransferCommand, Result<OrderDto>>
{
    private const int Attempts = 3;

    public async Task<Result<OrderDto>> HandleAsync(ApproveBankTransferCommand command, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (await store.OrderAsync(command.OrderId, ct) is not { } order) return CommerceErrors.OrderNotFound;
            var payment = order.SubmittedTransfer;
            var approved = order.ApproveBankTransfer(clock.GetUtcNow());
            if (approved.IsFailure) return approved.Error!;
            if (!approved.Value) return order.ToDto();

            await after.RecordAsync(order, payment!);
            try
            {
                await store.SaveAsync(ct);
            }
            catch (SaveConflictException) when (attempt < Attempts)
            {
                // Another approval of this order (unique invoice per order) or another order's invoice number won: reload.
                // The next pass sees the order paid and returns it unchanged.
                continue;
            }

            // Fulfilment happens only here, after the approval is saved (E5).
            await after.SendReceiptAsync(order, ct);
            await after.FulfilAsync(order, ct);
            return order.ToDto();
        }
    }
}

internal sealed class RejectBankTransferHandler(ICommerceStore store, TimeProvider clock)
    : ICommandHandler<RejectBankTransferCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> HandleAsync(RejectBankTransferCommand command, CancellationToken ct)
    {
        if (await store.OrderAsync(command.OrderId, ct) is not { } order) return CommerceErrors.OrderNotFound;
        var rejected = order.RejectBankTransfer(command.Reason, clock.GetUtcNow());
        if (rejected.IsFailure) return rejected.Error!;
        try
        {
            await store.SaveAsync(ct);
        }
        catch (SaveConflictException)
        {
            return CommerceErrors.NothingToVerify;
        }
        return order.ToDto();
    }
}
