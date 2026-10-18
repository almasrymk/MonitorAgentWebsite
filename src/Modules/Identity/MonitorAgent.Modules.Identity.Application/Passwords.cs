using FluentValidation;
using Microsoft.Extensions.Logging;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Identity.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Identity.Application;

/// <summary>Emails a reset link when the account exists. Always succeeds so the endpoint can't be used to discover accounts.</summary>
public sealed record ForgotPasswordCommand(string Email) : ICommand<Result>;

/// <summary>Sets a new password from an emailed link. Every existing session of the account is signed out.</summary>
public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword) : ICommand<Result>;

/// <summary>Changes the password of the signed-in account. Other sessions are signed out; this one stays.</summary>
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand<Result>;

internal sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator() => RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();
}

internal sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Token).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12).MaximumLength(128);
    }
}

internal sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12).MaximumLength(128);
    }
}

internal sealed class ForgotPasswordHandler(IUserAccounts users, IEmailSender email, IIdentityLinks links, ISecurityLog log, ILogger<ForgotPasswordHandler> logger)
    : ICommandHandler<ForgotPasswordCommand, Result>
{
    public async Task<Result> HandleAsync(ForgotPasswordCommand command, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(command.Email);
        if (user is null || !user.EmailConfirmed) return Result.Success();
        var token = await users.CreatePasswordResetTokenAsync(user.Id);
        await log.RecordAsync(user.Id, SecurityEventType.PasswordResetRequested, ct);
        await email.TrySendAsync(Emails.ResetPassword(user.Email, links.ResetPassword(user.Email, token)), logger, ct);
        return Result.Success();
    }
}

internal sealed class ResetPasswordHandler(IUserAccounts users, IEmailSender email, ISecurityLog log, ILogger<ResetPasswordHandler> logger) : ICommandHandler<ResetPasswordCommand, Result>
{
    public async Task<Result> HandleAsync(ResetPasswordCommand command, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(command.Email);
        if (user is null) return IdentityErrors.InvalidToken;
        var reset = await users.ResetPasswordAsync(user.Id, command.Token, command.NewPassword);
        if (reset.IsFailure) return reset;
        await users.RevokeSessionsAsync(user.Id);
        await log.RecordAsync(user.Id, SecurityEventType.PasswordReset, ct);
        await email.TrySendAsync(Emails.PasswordChanged(user.Email), logger, ct);
        return Result.Success();
    }
}

internal sealed class ChangePasswordHandler(IUserAccounts users, ISignInSession session, ICurrentUser me, IEmailSender email, ISecurityLog log,
    ILogger<ChangePasswordHandler> logger)
    : ICommandHandler<ChangePasswordCommand, Result>
{
    public async Task<Result> HandleAsync(ChangePasswordCommand command, CancellationToken ct)
    {
        if (me.UserId is not { } userId || await users.FindByIdAsync(userId) is not { } user) return IdentityErrors.Unauthenticated;
        var changed = await users.ChangePasswordAsync(userId, command.CurrentPassword, command.NewPassword);
        if (changed.IsFailure) return changed;
        await session.RefreshAsync(userId);
        // The password has changed; a lost notice must not report the change as failed.
        await log.RecordAsync(userId, SecurityEventType.PasswordChanged, ct);
        await email.TrySendAsync(Emails.PasswordChanged(user.Email), logger, ct);
        return Result.Success();
    }
}
