using FluentValidation;
using Microsoft.Extensions.Logging;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Commerce.Domain;
using MonitorAgent.Modules.Customers.Contracts;
using MonitorAgent.Modules.Licenses.Contracts;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Commerce.Application;

/// <summary>A provider's server-to-server payment notification, exactly as received.</summary>
public sealed record HandlePaymentNotificationCommand(string Provider, IReadOnlyDictionary<string, string> Headers, string Body) : ICommand<Result>;

/// <summary>Staff mark an order paid for money received outside the website. The license is issued right after.</summary>
public sealed record RecordManualPaymentCommand(Guid OrderId, string Reference) : ICommand<Result<OrderDto>>;

/// <summary>Issues licenses for paid orders whose fulfilment didn't finish (LicensingPlatform was down). Run on a timer.</summary>
public sealed record FulfillPaidOrdersCommand : ICommand<Result<int>>;

public sealed record ListOrdersQuery(OrderStatus? Status, int Page = 1, int PageSize = 50) : IQuery<Result<PagedResult<OrderDto>>>;

internal sealed class RecordManualPaymentValidator : AbstractValidator<RecordManualPaymentCommand>
{
    public RecordManualPaymentValidator() => RuleFor(x => x.Reference).NotEmpty().MaximumLength(100);
}

internal sealed class ListOrdersValidator : AbstractValidator<ListOrdersQuery>
{
    public ListOrdersValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

/// <summary>What happens once an order is paid: ledger line, invoice, then the license.</summary>
internal sealed class AfterPayment(ICommerceStore store, ICompanyDirectory companies, ILicenseFulfillment licenses, TimeProvider clock)
{
    public async Task RecordAsync(Order order, Payment payment)
    {
        var now = clock.GetUtcNow();
        store.Add(new LedgerEntry(order.Id, LedgerEntryKind.Payment, payment.Amount, payment.Currency, payment.Provider, payment.ProviderReference ?? "", now));
        var company = await companies.FindAsync(order.CompanyId, default);
        store.Add(new Invoice(await store.NextInvoiceNumberAsync(now.Year, default), order.Id, order.CompanyId, company?.Name ?? "",
            $"{order.ProductName} - {order.PlanName} ({order.Number})", order.Total, order.Currency, now));
    }

    /// <summary>Best effort: a failure leaves the order Paid, and <see cref="FulfillPaidOrdersCommand"/> retries it.</summary>
    public async Task FulfilAsync(Order order, CancellationToken ct)
    {
        var fulfilled = await licenses.FulfillAsync(order.CompanyId, order.PlanId, order.Number, ct);
        if (fulfilled.IsFailure) return;
        order.Fulfilled(fulfilled.Value.LicenseNumber);
        try
        {
            await store.SaveAsync(ct);
        }
        catch (SaveConflictException)
        {
            // The license exists; issuing is idempotent per order, so the next retry only records it.
        }
    }
}

internal sealed partial class HandlePaymentNotificationHandler(
    IEnumerable<IPaymentGateway> gateways, ICommerceStore store, AfterPayment after, TimeProvider clock, ILogger<HandlePaymentNotificationHandler> logger)
    : ICommandHandler<HandlePaymentNotificationCommand, Result>
{
    public async Task<Result> HandleAsync(HandlePaymentNotificationCommand command, CancellationToken ct)
    {
        var gateway = gateways.SingleOrDefault(g => g.Name == command.Provider);
        if (gateway is null) return CommerceErrors.UnknownProvider;
        var read = gateway.ReadNotification(command.Headers, command.Body);
        if (read.IsFailure) return read.Error!;
        var notification = read.Value;

        // Providers resend until they get a 2xx; a notification already handled is acknowledged and ignored.
        if (await store.NotificationSeenAsync(gateway.Name, notification.EventId, ct)) return Result.Success();

        var order = await store.OrderForPaymentAsync(gateway.Name, notification.ProviderReference, ct);
        store.Add(new ProcessedNotification(gateway.Name, notification.EventId, clock.GetUtcNow()));
        if (order is null)
        {
            UnknownPayment(logger, gateway.Name);
            await SaveAsync(ct);
            return Result.Success();
        }

        var payment = order.PaymentFor(gateway.Name, notification.ProviderReference)!;
        var paid = false;
        if (notification.Succeeded)
            paid = order.ConfirmPayment(payment, notification.Amount, notification.Currency, clock.GetUtcNow());
        else
            order.FailPayment(payment, notification.FailureReason ?? "Declined.", clock.GetUtcNow());
        if (paid) await after.RecordAsync(order, payment);

        try
        {
            await store.SaveAsync(ct);
        }
        catch (SaveConflictException)
        {
            // The same notification (or an invoice number) was taken by a parallel request. Ask the provider to retry.
            return CommerceErrors.PaymentUnavailable;
        }

        if (paid) await after.FulfilAsync(order, ct);
        return Result.Success();
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await store.SaveAsync(ct);
        }
        catch (SaveConflictException)
        {
            // A parallel delivery of the same notification already saved it.
        }
    }

    [LoggerMessage(LogLevel.Warning, "Payment notification from {Provider} for a checkout we don't know")]
    private static partial void UnknownPayment(ILogger logger, string provider);
}

internal sealed class RecordManualPaymentHandler(ICommerceStore store, AfterPayment after, TimeProvider clock)
    : ICommandHandler<RecordManualPaymentCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> HandleAsync(RecordManualPaymentCommand command, CancellationToken ct)
    {
        if (await store.OrderAsync(command.OrderId, ct) is not { } order) return CommerceErrors.OrderNotFound;
        var recorded = order.RecordManualPayment(command.Reference, clock.GetUtcNow());
        if (recorded.IsFailure) return recorded.Error!;
        await after.RecordAsync(order, order.Payments[^1]);
        await store.SaveAsync(ct);
        await after.FulfilAsync(order, ct);
        return order.ToDto();
    }
}

internal sealed class FulfillPaidOrdersHandler(ICommerceStore store, AfterPayment after) : ICommandHandler<FulfillPaidOrdersCommand, Result<int>>
{
    public async Task<Result<int>> HandleAsync(FulfillPaidOrdersCommand command, CancellationToken ct)
    {
        var done = 0;
        foreach (var order in await store.PaidUnfulfilledAsync(20, ct))
        {
            await after.FulfilAsync(order, ct);
            if (order.Status == OrderStatus.Fulfilled) done++;
        }
        return done;
    }
}

internal sealed class ListOrdersHandler(ICommerceStore store) : IQueryHandler<ListOrdersQuery, Result<PagedResult<OrderDto>>>
{
    public async Task<Result<PagedResult<OrderDto>>> HandleAsync(ListOrdersQuery query, CancellationToken ct)
    {
        var (items, total) = await store.OrdersAsync(query.Status, query.Page, query.PageSize, ct);
        return new PagedResult<OrderDto>(items.Select(o => o.ToDto()).ToList(), query.Page, query.PageSize, total);
    }
}
