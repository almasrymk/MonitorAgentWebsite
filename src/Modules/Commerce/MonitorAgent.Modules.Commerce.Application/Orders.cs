using System.Security.Cryptography;
using FluentValidation;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Commerce.Domain;
using MonitorAgent.Modules.Customers.Contracts;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.Modules.Licenses.Contracts;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Commerce.Application;

public sealed record PaymentDto(Guid Id, string Provider, decimal Amount, string Currency, PaymentStatus Status, DateTimeOffset CreatedAt, string? FailureReason);
public sealed record OrderDto(Guid Id, string Number, string ProductName, string PlanName, decimal Total, string Currency, OrderStatus Status,
    DateTimeOffset PlacedAt, DateTimeOffset? PaidAt, string? LicenseNumber, IReadOnlyList<PaymentDto> Payments, Guid? CompanyId = null,
    string? CompanyName = null);
public sealed record CheckoutDto(string RedirectUrl);
public sealed record InvoiceDto(Guid Id, string Number, Guid OrderId, string CompanyName, string Description, decimal Amount, string Currency, DateTimeOffset IssuedAt);

public sealed record PlaceOrderCommand(Guid PlanId) : ICommand<Result<OrderDto>>;
public sealed record PayOrderCommand(Guid OrderId, string Provider) : ICommand<Result<CheckoutDto>>;
public sealed record CancelOrderCommand(Guid OrderId) : ICommand<Result>, IAuditedCommand
{
    public string AuditAction => "order.cancel";
    public string AuditTarget => $"order:{OrderId}";
}
public sealed record ListMyOrdersQuery : IQuery<Result<IReadOnlyList<OrderDto>>>;
public sealed record GetMyOrderQuery(Guid OrderId) : IQuery<Result<OrderDto>>;
public sealed record ListMyInvoicesQuery : IQuery<Result<IReadOnlyList<InvoiceDto>>>;

internal sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator() => RuleFor(x => x.PlanId).NotEmpty();
}

internal sealed class PayOrderValidator : AbstractValidator<PayOrderCommand>
{
    public PayOrderValidator() => RuleFor(x => x.Provider).NotEmpty().MaximumLength(30);
}

internal static class OrderMapping
{
    public static OrderDto ToDto(this Order o) => new(o.Id, o.Number, o.ProductName, o.PlanName, o.Total, o.Currency, o.Status, o.PlacedAt, o.PaidAt,
        o.LicenseNumber, o.Payments.OrderBy(p => p.CreatedAt).Select(p => new PaymentDto(p.Id, p.Provider, p.Amount, p.Currency, p.Status, p.CreatedAt, p.FailureReason)).ToList());

    public static InvoiceDto ToDto(this Invoice i) => new(i.Id, i.Number, i.OrderId, i.CompanyName, i.Description, i.Amount, i.Currency, i.IssuedAt);

    /// <summary>Hard to guess and easy to read out on the phone: MA-261015-7KQ2XD.</summary>
    public static string NewOrderNumber(DateTimeOffset at)
    {
        const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        var suffix = new string(Enumerable.Range(0, 6).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        return $"MA-{at:yyMMdd}-{suffix}";
    }
}

/// <summary>The caller's company, and whether their role may buy (owners and billing members).</summary>
internal sealed class BuyerScope(ICurrentUser me, ICompanyDirectory companies)
{
    public async Task<Result<CompanyMembership>> ResolveAsync(CancellationToken ct)
    {
        if (me.UserId is not { } userId || await companies.MembershipOfAsync(userId, ct) is not { } membership) return CommerceErrors.NoCompany;
        if (membership.Role is not ("Owner" or "Billing")) return CommerceErrors.NotAllowed;
        return membership;
    }

    public async Task<Result<Order>> OwnOrderAsync(ICommerceStore store, Guid orderId, CancellationToken ct)
    {
        var buyer = await ResolveAsync(ct);
        if (buyer.IsFailure) return buyer.Error!;
        var order = await store.OrderAsync(orderId, ct);
        return order is not null && order.CompanyId == buyer.Value.CompanyId ? order : CommerceErrors.OrderNotFound;
    }
}

internal sealed class PlaceOrderHandler(BuyerScope scope, IPlanCatalog catalog, ICommerceStore store, ICurrentUser me, TimeProvider clock)
    : ICommandHandler<PlaceOrderCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> HandleAsync(PlaceOrderCommand command, CancellationToken ct)
    {
        var buyer = await scope.ResolveAsync(ct);
        if (buyer.IsFailure) return buyer.Error!;
        var offer = await catalog.OfferAsync(command.PlanId, ct);
        if (offer.IsFailure) return offer.Error!;

        var now = clock.GetUtcNow();
        var order = Order.Place(OrderMapping.NewOrderNumber(now), buyer.Value.CompanyId, me.UserId!.Value, offer.Value.PlanId,
            offer.Value.ProductName, offer.Value.PlanName, offer.Value.Price, offer.Value.Currency, now);
        store.Add(order);
        await store.SaveAsync(ct);
        return order.ToDto();
    }
}

internal sealed class PayOrderHandler(BuyerScope scope, ICommerceStore store, IEnumerable<IPaymentGateway> gateways, IUserDirectory users,
    ICurrentUser me, PortalLinks portal, TimeProvider clock) : ICommandHandler<PayOrderCommand, Result<CheckoutDto>>
{
    public async Task<Result<CheckoutDto>> HandleAsync(PayOrderCommand command, CancellationToken ct)
    {
        var gateway = gateways.SingleOrDefault(g => g.Name == command.Provider);
        if (gateway is null) return CommerceErrors.UnknownProvider;
        var found = await scope.OwnOrderAsync(store, command.OrderId, ct);
        if (found.IsFailure) return found.Error!;
        var order = found.Value;

        var payment = order.StartPayment(gateway.Name, clock.GetUtcNow());
        if (payment.IsFailure) return payment.Error!;
        await store.SaveAsync(ct);

        var email = (await users.FindAsync(me.UserId!.Value, ct))?.Email ?? "";
        try
        {
            var session = await gateway.CreateCheckoutAsync(new CheckoutRequest(order.Id, order.Number, payment.Value.Id, order.Total, order.Currency,
                $"{order.ProductName} - {order.PlanName}", email, portal.To($"orders/{order.Id}")), ct);
            order.CheckoutCreated(payment.Value.Id, session.ProviderReference);
            await store.SaveAsync(ct);
            return new CheckoutDto(session.RedirectUrl);
        }
        catch (PaymentProviderUnavailableException)
        {
            order.FailPayment(payment.Value, "Checkout could not be created.", clock.GetUtcNow());
            await store.SaveAsync(ct);
            return CommerceErrors.PaymentUnavailable;
        }
    }
}

internal sealed class CancelOrderHandler(BuyerScope scope, ICommerceStore store) : ICommandHandler<CancelOrderCommand, Result>
{
    public async Task<Result> HandleAsync(CancelOrderCommand command, CancellationToken ct)
    {
        var found = await scope.OwnOrderAsync(store, command.OrderId, ct);
        if (found.IsFailure) return found.Error!;
        var result = found.Value.Cancel();
        if (result.IsSuccess) await store.SaveAsync(ct);
        return result;
    }
}

internal sealed class ListMyOrdersHandler(BuyerScope scope, ICommerceStore store) : IQueryHandler<ListMyOrdersQuery, Result<IReadOnlyList<OrderDto>>>
{
    public async Task<Result<IReadOnlyList<OrderDto>>> HandleAsync(ListMyOrdersQuery query, CancellationToken ct)
    {
        var buyer = await scope.ResolveAsync(ct);
        if (buyer.IsFailure) return buyer.Error!;
        return (await store.OrdersOfAsync(buyer.Value.CompanyId, ct)).Select(o => o.ToDto()).ToList();
    }
}

internal sealed class GetMyOrderHandler(BuyerScope scope, ICommerceStore store) : IQueryHandler<GetMyOrderQuery, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> HandleAsync(GetMyOrderQuery query, CancellationToken ct)
    {
        var found = await scope.OwnOrderAsync(store, query.OrderId, ct);
        return found.IsFailure ? found.Error! : found.Value.ToDto();
    }
}

internal sealed class ListMyInvoicesHandler(BuyerScope scope, ICommerceStore store) : IQueryHandler<ListMyInvoicesQuery, Result<IReadOnlyList<InvoiceDto>>>
{
    public async Task<Result<IReadOnlyList<InvoiceDto>>> HandleAsync(ListMyInvoicesQuery query, CancellationToken ct)
    {
        var buyer = await scope.ResolveAsync(ct);
        if (buyer.IsFailure) return buyer.Error!;
        return (await store.InvoicesOfAsync(buyer.Value.CompanyId, ct)).Select(i => i.ToDto()).ToList();
    }
}
