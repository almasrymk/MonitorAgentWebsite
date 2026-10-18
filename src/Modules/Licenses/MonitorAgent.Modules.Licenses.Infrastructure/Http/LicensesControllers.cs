using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.BuildingBlocks.Web;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.Modules.Licenses.Application;
using MonitorAgent.Modules.Licenses.Contracts;

namespace MonitorAgent.Modules.Licenses.Infrastructure.Http;

/// <summary>The public price list.</summary>
[ApiController]
[Route("api/v1/catalog")]
[AllowAnonymous]
public sealed class CatalogController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("plans")]
    [ProducesResponseType<IReadOnlyList<CatalogPlanDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Plans(CancellationToken ct) => (await dispatcher.QueryAsync(new GetCatalogQuery(), ct)).ToActionResult();
}

/// <summary>The signed-in customer's company licenses and devices. No route can name another company.</summary>
[ApiController]
[Route("api/v1/licenses")]
[Authorize(Policy = AuthPolicies.Customer)]
public sealed class MyLicensesController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<LicenseSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => (await dispatcher.QueryAsync(new ListMyLicensesQuery(), ct)).ToActionResult();

    [HttpGet("{licenseId:guid}")]
    [ProducesResponseType<LicenseDetailsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid licenseId, CancellationToken ct) =>
        (await dispatcher.QueryAsync(new GetMyLicenseQuery(licenseId), ct)).ToActionResult();

    /// <summary>A POST so the key never sits in a cached GET response or a browser history entry.</summary>
    [HttpPost("{licenseId:guid}/product-key")]
    [ProducesResponseType<ProductKeyDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevealKey(Guid licenseId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return (await dispatcher.SendAsync(new RevealProductKeyCommand(licenseId), ct)).ToActionResult();
    }

    [HttpPost("{licenseId:guid}/devices/{deviceId:guid}/reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetDevice(Guid licenseId, Guid deviceId, CancellationToken ct) =>
        (await dispatcher.SendAsync(new ResetDeviceCommand(licenseId, deviceId), ct)).ToActionResult();
}

public sealed record GrantRequest(Guid CompanyId, Guid PlanId, string OrderReference);

/// <summary>Staff issue a license by hand, for a payment taken outside the website (bank transfer, cash).</summary>
[ApiController]
[Route("api/v1/admin/licenses")]
[Authorize(Policy = AuthPolicies.StaffAdmin)]
public sealed class AdminLicensesController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost("grants")]
    [ProducesResponseType<FulfilledLicense>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Grant(GrantRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new FulfillLicenseCommand(request.CompanyId, request.PlanId, request.OrderReference), ct)).ToActionResult();
}

/// <summary>Admins, support and finance: the licenses of one customer company.</summary>
[ApiController]
[Route("api/v1/admin/customers/{companyId:guid}/licenses")]
[Authorize(Policy = AuthPolicies.StaffCustomers)]
public sealed class AdminCompanyLicensesController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<LicenseSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid companyId, CancellationToken ct) =>
        (await dispatcher.QueryAsync(new CompanyLicensesQuery(companyId), ct)).ToActionResult();
}
