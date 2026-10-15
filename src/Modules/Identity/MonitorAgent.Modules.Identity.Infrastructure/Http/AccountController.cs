using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.BuildingBlocks.Web;
using MonitorAgent.Modules.Identity.Application;
using MonitorAgent.Modules.Identity.Contracts;

namespace MonitorAgent.Modules.Identity.Infrastructure.Http;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record MfaCodeRequest(string Code);
public sealed record PasswordRequest(string Password);

/// <summary>The signed-in user's own account and security settings (customers and staff alike).</summary>
[ApiController]
[Route("api/v1/account")]
[Authorize]
public sealed class AccountController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<MeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct) => (await dispatcher.QueryAsync(new GetMeQuery(), ct)).ToActionResult();

    [HttpPost("change-password")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new ChangePasswordCommand(request.CurrentPassword, request.NewPassword), ct)).ToActionResult();

    [HttpPost("sessions/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeSessions(CancellationToken ct) =>
        (await dispatcher.SendAsync(new RevokeAllSessionsCommand(), ct)).ToActionResult();

    /// <summary>Everything the website holds about the signed-in person, as a JSON file.</summary>
    [HttpGet("export")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<PersonalDataExport>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var export = await dispatcher.QueryAsync(new ExportMyDataQuery(), ct);
        if (export.IsFailure) return export.ToActionResult();
        Response.Headers.CacheControl = "no-store";
        Response.Headers.ContentDisposition = $"attachment; filename=\"monitor-agent-data-{export.Value.ExportedAt:yyyy-MM-dd}.json\"";
        return Ok(export.Value);
    }

    /// <summary>Deletes the signed-in customer's account for good. Asks for the password again.</summary>
    [HttpPost("delete")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(PasswordRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new DeleteMyAccountCommand(request.Password), ct)).ToActionResult();

    [HttpGet("mfa/setup")]
    [ProducesResponseType<AuthenticatorSetup>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MfaSetup(CancellationToken ct) => (await dispatcher.QueryAsync(new GetMfaSetupQuery(), ct)).ToActionResult();

    [HttpPost("mfa/enable")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<RecoveryCodesDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EnableMfa(MfaCodeRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new EnableMfaCommand(request.Code), ct)).ToActionResult();

    [HttpPost("mfa/disable")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DisableMfa(PasswordRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new DisableMfaCommand(request.Password), ct)).ToActionResult();

    [HttpPost("mfa/recovery-codes")]
    [ProducesResponseType<RecoveryCodesDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RecoveryCodes(CancellationToken ct) =>
        (await dispatcher.SendAsync(new RegenerateRecoveryCodesCommand(), ct)).ToActionResult();
}

/// <summary>Staff administration of accounts. Admins only, and only from a session that passed two-step verification.</summary>
[ApiController]
[Route("api/v1/admin/security-events")]
[Authorize(Policy = AuthPolicies.StaffAdmin)]
public sealed class SecurityEventsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<SecurityEventDto>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<SecurityEventDto>> List([FromQuery] Guid? userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        await dispatcher.QueryAsync(new ListSecurityEventsQuery(userId, page, pageSize), ct);
}
