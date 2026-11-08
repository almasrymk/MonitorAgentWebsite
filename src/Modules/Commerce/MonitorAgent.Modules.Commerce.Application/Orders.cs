using System.Security.Cryptography;
using FluentValidation;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Commerce.Domain;
using MonitorAgent.Modules.Customers.Contracts;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.Modules.Licenses.Contracts;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Commerce.Application;

/// <summary><paramref name="Reference"/> and <paramref name="Note"/> are shown for bank transfers and manual payments only.</summary>
public sealed record PaymentDto(Guid Id, string Provider, decimal Amount, string Currency, PaymentStatus Status, DateTimeOffset CreatedAt, string? FailureReason,
    string? Reference = null, string? Note = null);
public sealed record OrderDto(Guid Id, string Number, string ProductName, string PlanName, decimal Total, string Currency, OrderStatus Status,
    DateTimeOffset PlacedAt, DateTimeOffset? PaidAt, string? LicenseNumber, IReadOnlyList<PaymentDto> Payments, Guid? CompanyId = null,
    string? CompanyName = null, OrderKind Kind = OrderKind.Purchase, Guid? RenewsGrantId = null, decimal RefundedAmount = 0,
    DisputeStatus? Dispute = null, DateTimeOffset? DisputedAt = null, string? DisputeReason = null);
public sealed record CheckoutDto(string RedirectUrl);
public sealed record InvoiceDto(Guid Id, string Number, Guid OrderId, string CompanyName, string Description, decimal Amount, string Currency, DateTimeOffset IssuedAt);

public sealed record PlaceOrderCommand(Guid PlanId) : ICommand<Result<OrderDto>>;
/// <summary>INT-018: an order to renew one of the company's licenses. Paid like any order; the term is extended on payment.</summary>
public sealed record PlaceRenewalOrderCommand(Guid LicenseId) : ICommand<Result<OrderDto>>;
/// <summary>INT-014: what upgrading the subscription to the plan costs now, for the rest of its term.</summary>
public sealed record UpgradeQuoteDto(Guid SubscriptionId, Guid FromPlanId, string FromPlanName, Guid PlanId, string PlanName, decimal Price, string Currency,
    int RemainingDays, int TermDays, DateTimeOffset TermEndsAt);
public sealed record GetUpgradeQuoteQuery(Guid SubscriptionId, Guid PlanId) : IQuery<Result<UpgradeQuoteDto>>;
public sealed record PlaceUpgradeOrderCommand(Guid SubscriptionId, Guid PlanId) : ICommand<Result<OrderDto>>;
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
        o.LicenseNumber, o.Payments.OrderBy(p => p.CreatedAt).Select(p => new PaymentDto(p.Id, p.Provider, p.Amount, p.Currency, p.Status, p.CreatedAt, p.FailureReason,
            p.Provider is "manual" or Order.BankTransfer ? p.ProviderReference : null, p.CustomerNote)).ToList(),
        Kind: o.Kind, RenewsGrantId: o.RenewsGrantId, RefundedAmount: o.RefundedAmount,
        Dispute: o.Dispute, DisputedAt: o.DisputedAt, DisputeReason: o.DisputeReason);

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

internal sealed class PlaceRenewalOrderValidator : AbstractValidator<PlaceRenewalOrderCommand>
{
    public PlaceRenewalOrderValidator() => RuleFor(x => x.LicenseId).NotEmpty();
}

internal sealed class PlaceRenewalOrderHandler(BuyerScope scope, ILicenseRenewals renewals, ICommerceStore store, ICurrentUser me, TimeProvider clock)
    : ICommandHandler<PlaceRenewalOrderCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> HandleAsync(PlaceRenewalOrderCommand command, CancellationToken ct)
    {
        var buyer = await scope.ResolveAsync(ct);
        if (buyer.IsFailure) return buyer.Error!;
        var offer = await renewals.OfferAsync(buyer.Value.CompanyId, command.LicenseId, ct);
        if (offer.IsFailure) return offer.Error!;

        var now = clock.GetUtcNow();
        var order = Order.PlaceRenewal(OrderMapping.NewOrderNumber(now), buyer.Value.CompanyId, me.UserId!.Value, offer.Value.GrantId, offer.Value.PlanId,
            offer.Value.ProductName, offer.Value.PlanName, offer.Value.Price, offer.Value.Currency, now);
        store.Add(order);
        await store.SaveAsync(ct);
        return order.ToDto();
    }
}

internal sealed class PlaceUpgradeOrderValidator : AbstractValidator<PlaceUpgradeOrderCommand>
{
    public PlaceUpgradeOrderValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
    }
}

internal sealed class GetUpgradeQuoteValidator : AbstractValidator<GetUpgradeQuoteQuery>
{
    public GetUpgradeQuoteValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
    }
}

internal sealed class GetUpgradeQuoteHandler(BuyerScope scope, ILicenseUpgrades upgrades) : IQueryHandler<GetUpgradeQuoteQuery, Result<UpgradeQuoteDto>>
{
    public async Task<Result<UpgradeQuoteDto>> HandleAsync(GetUpgradeQuoteQuery query, CancellationToken ct)
    {
        var buyer = await scope.ResolveAsync(ct);
        if (buyer.IsFailure) return buyer.Error!;
        var offer = await upgrades.QuoteAsync(buyer.Value.CompanyId, query.SubscriptionId, query.PlanId, ct);
        if (offer.IsFailure) return offer.Error!;
        var o = offer.Value;
        return new UpgradeQuoteDto(o.SubscriptionId, o.FromPlanId, o.FromPlanName, o.PlanId, o.PlanName, o.Price, o.Currency, o.RemainingDays, o.TermDays, o.TermEndsAt);
    }
}

/// <summary>
/// INT-014: places a prorated upgrade order at today's quote. The plan is not changed here: only a paid (and, for a
/// bank transfer, approved) order is fulfilled, by <see cref="AfterPayment"/>. One open upgrade order per subscription.
/// </summary>
internal sealed class PlaceUpgradeOrderHandler(BuyerScope scope, ILicenseUpgrades upgrades, ICommerceStore store, ICurrentUser me, TimeProvider clock)
    : ICommandHandler<PlaceUpgradeOrderCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> HandleAsync(PlaceUpgradeOrderCommand command, CancellationToken ct)
    {
        var buyer = await scope.ResolveAsync(ct);
        if (buyer.IsFailure) return buyer.Error!;
        var offer = await upgrades.QuoteAsync(buyer.Value.CompanyId, command.SubscriptionId, command.PlanId, ct);
        if (offer.IsFailure) return offer.Error!;
        var o = offer.Value;
        if ((await store.OrdersOfAsync(buyer.Value.CompanyId, ct)).Any(x => x.Kind == OrderKind.Upgrade && x.RenewsGrantId == o.GrantId && x.IsOpen))
            return CommerceErrors.UpgradeAlreadyOpen;

        var now = clock.GetUtcNow();
        var order = Order.PlaceUpgrade(OrderMapping.NewOrderNumber(now), buyer.Value.CompanyId, me.UserId!.Value, o.GrantId, o.PlanId,
            o.ProductName, UpgradeName(o.FromPlanName, o.PlanName), o.Price, o.Currency, now);
        store.Add(order);
        await store.SaveAsync(ct);
        return order.ToDto();
    }

    /// <summary>"Standard → Plus", within the 150 characters the order keeps for a plan name.</summary>
    internal static string UpgradeName(string from, string to)
    {
        var name = $"{from} → {to}";
        return name.Length <= 150 ? name : name[..150];
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
