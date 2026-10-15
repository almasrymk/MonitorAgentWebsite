using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.BuildingBlocks.Web;
using MonitorAgent.Modules.Identity.Application;

namespace MonitorAgent.Modules.Identity.Infrastructure.Http;

public sealed record RegisterRequest(string Email, string Password, string FullName);
public sealed record EmailRequest(string Email);
public sealed record ConfirmEmailRequest(Guid UserId, string Token);
public sealed record SignInRequest(string Email, string Password, bool RememberMe = false);
public sealed record MfaSignInRequest(string Code, bool IsRecoveryCode = false, bool RememberMe = false);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

/// <summary>
/// Anonymous account flows. Every write needs the CSRF header (GET /api/v1/auth/csrf issues it) and the stricter
/// <see cref="RateLimitPolicies.Auth"/> budget applies.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class AuthController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new RegisterCommand(request.Email, request.Password, request.FullName), ct)).ToAccepted();

    [HttpPost("confirm-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new ConfirmEmailCommand(request.UserId, request.Token), ct)).ToActionResult();

    [HttpPost("resend-confirmation")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ResendConfirmation(EmailRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new ResendConfirmationCommand(request.Email), ct)).ToAccepted();

    [HttpPost("login")]
    [ProducesResponseType<SignInResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(SignInRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new SignInCommand(request.Email, request.Password, request.RememberMe), ct)).ToActionResult();

    [HttpPost("login/mfa")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LoginMfa(MfaSignInRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new SignInWithMfaCommand(request.Code, request.IsRecoveryCode, request.RememberMe), ct)).ToActionResult();

    [HttpPost("logout")]
    [DisableRateLimiting]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct) =>
        (await dispatcher.SendAsync(new SignOutCommand(), ct)).ToActionResult();

    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new ForgotPasswordCommand(request.Email), ct)).ToAccepted();

    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new ResetPasswordCommand(request.Email, request.Token, request.NewPassword), ct)).ToActionResult();
}
