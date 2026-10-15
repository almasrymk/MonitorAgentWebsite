using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.BuildingBlocks.Web;
using MonitorAgent.Modules.Customers.Application;
using MonitorAgent.Modules.Identity.Contracts;

namespace MonitorAgent.Modules.Customers.Infrastructure.Http;

/// <summary>Admins, support and finance (signed in with two-step verification): every customer company and its people. Read only.</summary>
[ApiController]
[Route("api/v1/admin/customers")]
[Authorize(Policy = AuthPolicies.StaffCustomers)]
public sealed class AdminCustomersController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<CompanyListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        (await dispatcher.QueryAsync(new SearchCompaniesQuery(search, page, pageSize), ct)).ToActionResult();

    [HttpGet("{companyId:guid}")]
    [ProducesResponseType<CompanyDetailsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid companyId, CancellationToken ct) =>
        (await dispatcher.QueryAsync(new GetCompanyForStaffQuery(companyId), ct)).ToActionResult();
}
