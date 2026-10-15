using FluentValidation;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Identity.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Identity.Application;

public sealed record SignInResponse(bool RequiresMfa);

public sealed record SignInCommand(string Email, string Password, bool RememberMe) : ICommand<Result<SignInResponse>>;

/// <summary>Second step of sign-in, after a password sign-in answered <c>requiresMfa</c>. Takes an authenticator or recovery code.</summary>
public sealed record SignInWithMfaCommand(string Code, bool IsRecoveryCode, bool RememberMe) : ICommand<Result>;

public sealed record SignOutCommand : ICommand<Result>;

/// <summary>Signs the account out everywhere, this session included.</summary>
public sealed record RevokeAllSessionsCommand : ICommand<Result>;

internal sealed class SignInValidator : AbstractValidator<SignInCommand>
{
    public SignInValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

internal sealed class SignInWithMfaValidator : AbstractValidator<SignInWithMfaCommand>
{
    public SignInWithMfaValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
}

internal sealed class SignInHandler(ISignInSession session, ISecurityLog log) : ICommandHandler<SignInCommand, Result<SignInResponse>>
{
    public async Task<Result<SignInResponse>> HandleAsync(SignInCommand command, CancellationToken ct)
    {
        var (status, userId) = await session.PasswordAsync(command.Email.Trim(), command.Password, command.RememberMe);
        switch (status)
        {
            case SignInStatus.Succeeded:
                await log.RecordAsync(userId, SecurityEventType.SignedIn, ct);
                return new SignInResponse(RequiresMfa: false);
            case SignInStatus.RequiresMfa:
                return new SignInResponse(RequiresMfa: true);
            case SignInStatus.NotConfirmed:
                return IdentityErrors.EmailNotConfirmed;
            case SignInStatus.LockedOut:
                await log.RecordAsync(userId, SecurityEventType.LockedOut, ct);
                return IdentityErrors.LockedOut;
            default:
                await log.RecordAsync(userId, SecurityEventType.SignInFailed, ct);
                return IdentityErrors.InvalidCredentials;
        }
    }
}

internal sealed class SignInWithMfaHandler(ISignInSession session, ISecurityLog log) : ICommandHandler<SignInWithMfaCommand, Result>
{
    public async Task<Result> HandleAsync(SignInWithMfaCommand command, CancellationToken ct)
    {
        // Recovery codes keep their dash (it is part of the stored code); authenticator codes are digits only.
        var code = command.Code.Replace(" ", "", StringComparison.Ordinal);
        if (!command.IsRecoveryCode) code = code.Replace("-", "", StringComparison.Ordinal);
        var (status, userId) = await session.SecondFactorAsync(code, command.IsRecoveryCode, command.RememberMe);
        switch (status)
        {
            case SignInStatus.Succeeded:
                await log.RecordAsync(userId, command.IsRecoveryCode ? SecurityEventType.RecoveryCodeUsed : SecurityEventType.SignedIn, ct);
                return Result.Success();
            case SignInStatus.LockedOut:
                await log.RecordAsync(userId, SecurityEventType.LockedOut, ct);
                return IdentityErrors.LockedOut;
            case SignInStatus.Failed when userId is null:
                return IdentityErrors.MfaSessionExpired;
            default:
                await log.RecordAsync(userId, SecurityEventType.SignInFailed, ct);
                return IdentityErrors.InvalidMfaCode;
        }
    }
}

internal sealed class SignOutHandler(ISignInSession session, ICurrentUser me, ISecurityLog log) : ICommandHandler<SignOutCommand, Result>
{
    public async Task<Result> HandleAsync(SignOutCommand command, CancellationToken ct)
    {
        var userId = me.UserId;
        await session.SignOutAsync();
        if (userId is not null) await log.RecordAsync(userId, SecurityEventType.SignedOut, ct);
        return Result.Success();
    }
}

internal sealed class RevokeAllSessionsHandler(IUserAccounts users, ISignInSession session, ICurrentUser me, ISecurityLog log)
    : ICommandHandler<RevokeAllSessionsCommand, Result>
{
    public async Task<Result> HandleAsync(RevokeAllSessionsCommand command, CancellationToken ct)
    {
        if (me.UserId is not { } userId) return IdentityErrors.Unauthenticated;
        await users.RevokeSessionsAsync(userId);
        await session.SignOutAsync();
        await log.RecordAsync(userId, SecurityEventType.SessionsRevoked, ct);
        return Result.Success();
    }
}
