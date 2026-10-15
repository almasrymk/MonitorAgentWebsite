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

/// <summary>The payment providers checkout can send a customer to, in the order the portal offers them.</summary>
[ApiController]
[Route("api/v1/payments/providers")]
[Authorize(Policy = AuthPolicies.Customer)]
public sealed class PaymentProvidersController(IEnumerable<IPaymentGateway> gateways) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public IActionResult List() => Ok(gateways.Select(g => g.Name).ToList());
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
[Authorize(Policy = AuthPolicies.StaffFinance)]
public sealed class AdminOrdersController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<OrderDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] OrderStatus? status, [FromQuery] Guid? companyId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        (await dispatcher.QueryAsync(new ListOrdersQuery(status, page, pageSize, companyId), ct)).ToActionResult();

    [HttpPost("{orderId:guid}/manual-payment")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ManualPayment(Guid orderId, ManualPaymentRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new RecordManualPaymentCommand(orderId, request.Reference), ct)).ToActionResult();
}

/// <summary>Finance and admins: money received and refunded, from the ledger.</summary>
[ApiController]
[Route("api/v1/admin/reports")]
[Authorize(Policy = AuthPolicies.StaffFinance)]
public sealed class AdminReportsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("revenue")]
    [ProducesResponseType<RevenueReportDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Revenue([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        (await dispatcher.QueryAsync(new RevenueReportQuery(from, to), ct)).ToActionResult();

    [HttpGet("ledger.csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> Ledger([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        var file = await dispatcher.QueryAsync(new LedgerExportQuery(from, to), ct);
        if (file.IsFailure) return file.ToActionResult();
        // A byte order mark so Excel opens Arabic company names correctly.
        return File([.. System.Text.Encoding.UTF8.GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes(file.Value.Content)], "text/csv; charset=utf-8",
            file.Value.FileName);
    }
}
