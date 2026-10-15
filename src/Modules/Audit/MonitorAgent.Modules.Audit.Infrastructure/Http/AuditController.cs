using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.BuildingBlocks.Web;
using MonitorAgent.Modules.Audit.Application;
using MonitorAgent.Modules.Identity.Contracts;

namespace MonitorAgent.Modules.Audit.Infrastructure.Http;

/// <summary>Admins only: who did what, when and from where.</summary>
[ApiController]
[Route("api/v1/admin/audit")]
[Authorize(Policy = AuthPolicies.StaffAdmin)]
public sealed class AuditController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? action, [FromQuery] Guid? actorUserId, [FromQuery] string? target,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        (await dispatcher.QueryAsync(new ListAuditQuery(action, actorUserId, target, page, pageSize), ct)).ToActionResult();
}
