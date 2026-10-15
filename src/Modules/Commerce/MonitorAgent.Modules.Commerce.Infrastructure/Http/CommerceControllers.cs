using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.BuildingBlocks.Web;
using MonitorAgent.Modules.Commerce.Application;
using MonitorAgent.Modules.Commerce.Domain;
using MonitorAgent.Modules.Identity.Contracts;

namespace MonitorAgent.Modules.Commerce.Infrastructure.Http;

public sealed record PlaceOrderRequest(Guid PlanId);
public sealed record PayRequest(string Provider);
public sealed record ManualPaymentRequest(string Reference);

/// <summary>The signed-in company's orders and invoices. Owners and billing members only.</summary>
[ApiController]
[Route("api/v1")]
[Authorize(Policy = AuthPolicies.Customer)]
public sealed class OrdersController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost("orders")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new PlaceOrderCommand(request.PlanId), ct)).ToActionResult();

    [HttpGet("orders")]
    [ProducesResponseType<IReadOnlyList<OrderDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => (await dispatcher.QueryAsync(new ListMyOrdersQuery(), ct)).ToActionResult();

    [HttpGet("orders/{orderId:guid}")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid orderId, CancellationToken ct) => (await dispatcher.QueryAsync(new GetMyOrderQuery(orderId), ct)).ToActionResult();

    /// <summary>Starts a hosted checkout and returns the provider page to send the customer to.</summary>
    [HttpPost("orders/{orderId:guid}/pay")]
    [ProducesResponseType<CheckoutDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pay(Guid orderId, PayRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new PayOrderCommand(orderId, request.Provider), ct)).ToActionResult();

    [HttpPost("orders/{orderId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid orderId, CancellationToken ct) =>
        (await dispatcher.SendAsync(new CancelOrderCommand(orderId), ct)).ToActionResult();

    [HttpGet("invoices")]
    [ProducesResponseType<IReadOnlyList<InvoiceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoices(CancellationToken ct) => (await dispatcher.QueryAsync(new ListMyInvoicesQuery(), ct)).ToActionResult();
}

/// <summary>
/// Where payment providers report results. No session and no CSRF header: the signature in the request is the only
/// proof of who sent it, and the handler checks it before reading anything else.
/// </summary>
[ApiController]
[Route("api/v1/payments/notifications")]
[AllowAnonymous]
[NoCsrf]
[DisableRateLimiting]
public sealed class PaymentNotificationsController(IDispatcher dispatcher) : ControllerBase
{
    private const int MaxBodyBytes = 64 * 1024;

    [HttpPost("{provider}")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Receive(string provider, CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var result = await dispatcher.SendAsync(new HandlePaymentNotificationCommand(provider, headers, body), ct);
        return result.IsSuccess ? Ok() : result.ToActionResult();
    }
}

/// <summary>Finance and admins: every order, and payments received outside the website.</summary>
[ApiController]
[Route("api/v1/admin/orders")]
[Authorize(Policy = AuthPolicies.StaffAdmin)]
public sealed class AdminOrdersController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<OrderDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] OrderStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        (await dispatcher.QueryAsync(new ListOrdersQuery(status, page, pageSize), ct)).ToActionResult();

    [HttpPost("{orderId:guid}/manual-payment")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ManualPayment(Guid orderId, ManualPaymentRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new RecordManualPaymentCommand(orderId, request.Reference), ct)).ToActionResult();
}
